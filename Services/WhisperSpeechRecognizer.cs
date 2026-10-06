using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace MovaCore.Services
{
    /// <summary>
    /// Whisper through Whisper.net (whisper.cpp). The native runtime ships in runtimes\win-{arch} (CPU) and
    /// runtimes\vulkan\win-x64 (GPU) next to the exe. Whisper.net loads a runtime once per process and caches a failure
    /// for good, so the runtime is chosen here, before Whisper.net is touched, and forced. Whisper.net loads the model;
    /// transcription calls whisper.cpp itself (<see cref="WhisperNative"/>) to keep one whisper state for every phrase.
    /// Voice activity detection (whisper.cpp's Silero VAD, model in models\ next to the exe) lives here too, since it
    /// needs that same runtime.
    /// </summary>
    public sealed partial class WhisperSpeechRecognizer : ISpeechRecognizer, ISpeechDetector
    {
        /// <summary>The Silero VAD model, shipped in models\ next to the exe.</summary>
        public const string VadModelFileName = "ggml-silero-v6.2.0.bin";

        private const ulong XSTATE_MASK_AVX = 1UL << 2;
        private const int RelationProcessorCore = 0;

        // whisper_full_with_state's result when the cache of several decoders (beam search, or the fallback that samples
        // five candidates) does not fit in memory: it then frees the state it was given
        private const int StateFreedByWhisper = -7;

        // Load order of a runtime folder: dependencies first, as Whisper.net does it
        private static readonly string[] RuntimeFiles =
        {
            "ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-vulkan-whisper.dll", "ggml-whisper.dll", "whisper.dll",
        };

        private static readonly object RuntimeLock = new();
        private static readonly HashSet<string> LoggedNativeMessages = new(StringComparer.Ordinal);
        private static RuntimeLibrary? _runtime;
        private static IDisposable? _nativeLog;

        // Read once: the cores do not change while the app runs
        private static readonly Lazy<int> PhysicalCores = new(CountPhysicalCores);

        private readonly SemaphoreSlim _lock = new(1, 1);
        private WhisperFactory? _factory;
        private IntPtr _context; // the model's whisper_context, owned by _factory
        private IntPtr _state;   // its whisper_state: compute buffers, on the model's device
        private string? _loadedModel;
        private bool _loadedOnGpu;
        private GpuDevice? _loadedGpu; // null on the CPU, or when Vulkan lists no card
        private bool _beamSearch;
        private bool _warmedUp;
        private WhisperVadFactory? _vadFactory;
        private WhisperVadProcessor? _vad;

        public static string VadModelPath { get; } = Path.Combine(AppContext.BaseDirectory, "models", VadModelFileName);

        /// <summary>The processor's physical cores, 0 if Windows could not tell (for the smoke test).</summary>
        internal static int PhysicalCoreCount => PhysicalCores.Value;

        /// <summary>How many whisper states were created: one per model load (for the smoke test).</summary>
        internal int StatesCreated { get; private set; }

        public Task PreloadAsync(SpeechOptions options, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    EnsureModelLoaded(options);
                    if (!_warmedUp)
                    {
                        _warmedUp = true;
                        WarmUp(options, cancellationToken);
                    }
                }
                finally
                {
                    _lock.Release();
                }
            }, cancellationToken);

        public Task<IReadOnlyList<SpeechSegment>?> DetectSpeechAsync(float[] samples, SpeechOptions options, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                if (!File.Exists(VadModelPath)) return null;

                await _lock.WaitAsync(cancellationToken);
                try
                {
                    WhisperVadProcessor vad = EnsureVadLoaded(options);
                    var segments = new List<SpeechSegment>();
                    foreach (VadSegmentData segment in vad.DetectSpeech(samples))
                        segments.Add(new SpeechSegment(segment.Start, segment.End));
                    return (IReadOnlyList<SpeechSegment>?)segments;
                }
                finally
                {
                    _lock.Release();
                }
            }, cancellationToken);

        public Task<IReadOnlyList<string>> TranscribeAsync(float[] samples, SpeechOptions options, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    int audioContext = options.FastRecognition ? WhisperAudioContext.For(samples.Length) : WhisperAudioContext.Full;
                    // Synchronous: the lock covers the native call, or the model could be freed underneath it
                    return Transcribe(samples, options, audioContext, cancellationToken);
                }
                finally
                {
                    _lock.Release();
                }
            }, cancellationToken);

        public Task UnloadAsync() =>
            Task.Run(async () =>
            {
                await _lock.WaitAsync();
                try
                {
                    if (_factory == null) return;
                    Free();
                    AppLog.Info("Speech model unloaded");
                }
                finally
                {
                    _lock.Release();
                }
            });

        /// <summary>
        /// Chooses and loads the native runtime, as the first dictation would, and describes it (for the smoke test).
        /// </summary>
        public static string LoadRuntime(bool useGpu)
        {
            RuntimeLibrary runtime = ChooseRuntime(useGpu);
            return $"{runtime}: {WhisperFactory.GetRuntimeInfo()?.Trim()}";
        }

        /// <summary>
        /// The first run on a GPU compiles its shaders, which can take seconds: better while the app starts than on the
        /// first dictation. On the CPU there is nothing to gain, and a run would only keep every core busy.
        /// </summary>
        private void WarmUp(SpeechOptions options, CancellationToken cancellationToken)
        {
            if (!_loadedOnGpu) return;
            long started = Stopwatch.GetTimestamp();
            var silence = new float[AudioSamples.SampleRate * 5 / 4];
            // The audio context short phrases use, or the whole window
            Transcribe(silence, options, options.FastRecognition ? WhisperAudioContext.Step : WhisperAudioContext.Full, cancellationToken);
            AppLog.Info($"Speech model warmed up in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");
        }

        /// <summary>
        /// Recognizes the samples with the loaded model and its one state, and returns the text in the parts Whisper
        /// marked with timestamps (<see cref="WhisperNative.ReadParts"/>). whisper.cpp takes the audio context from the
        /// parameters of each call, and the state's buffers are sized for the whole window, so every size shares it.
        /// The parameters are those Whisper.net set before (its defaults plus what its builder was asked for), except that
        /// a large discrete graphics card decodes with beam search (<see cref="GpuChoice.UseBeamSearch"/>): five
        /// candidates instead of one, more accurate for a fraction of a second there, while on a processor or a small card
        /// the decoder would cost several times as much.
        /// </summary>
        private unsafe IReadOnlyList<string> Transcribe(
            float[] samples, SpeechOptions options, int audioContext, CancellationToken cancellationToken)
        {
            EnsureModelLoaded(options);
            if (samples.Length == 0) return Array.Empty<string>();

            WhisperFullParams parameters = WhisperNative.DefaultParams(
                _beamSearch ? WhisperNative.SamplingBeamSearch : WhisperNative.SamplingGreedy);
            parameters.Threads = ThreadCount(WhisperThreads.Max);
            parameters.NoContext = 1;
            parameters.PrintProgress = 0;
            if (audioContext < WhisperAudioContext.Full)
            {
                // A short context makes Whisper prone to repeating the phrase once it is done: one segment, and a
                // ceiling of ~15 tokens a second, far above speech, cut that loop short (TranscriptText drops the repeat)
                parameters.AudioContext = audioContext;
                parameters.SingleSegment = 1;
                parameters.MaxTokens = audioContext / 4;
            }

            byte[] language = Encoding.UTF8.GetBytes(options.Language + "\0");
            GCHandle cancellation = GCHandle.Alloc(cancellationToken);
            try
            {
                int result;
                fixed (byte* languageUtf8 = language)
                fixed (float* audio = samples)
                {
                    parameters.Language = (IntPtr)languageUtf8;
                    parameters.AbortCallback = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte>)&ShouldAbort;
                    parameters.AbortCallbackUserData = GCHandle.ToIntPtr(cancellation);
                    result = WhisperNative.Full(_context, _state, parameters, audio, samples.Length);
                }
                if (result == StateFreedByWhisper)
                {
                    // Already freed: only the model is left to free, and the next dictation loads it again
                    _state = IntPtr.Zero;
                    Free();
                    throw new InvalidOperationException("whisper.cpp ran out of memory for decoding and freed its state");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (result != 0) throw new InvalidOperationException($"whisper.cpp could not transcribe (error {result})");
            }
            finally
            {
                cancellation.Free();
            }
            return WhisperNative.ReadParts(_context, _state);
        }

        // whisper.cpp asks before each computation whether to stop, so a cancelled dictation ends within a moment
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte ShouldAbort(IntPtr cancellation) =>
            GCHandle.FromIntPtr(cancellation).Target is CancellationToken { IsCancellationRequested: true } ? (byte)1 : (byte)0;

        // Settings close to Handy's (Silero with smoothing): a lenient threshold, since losing speech is worse than
        // keeping a little silence, and padding that keeps the edges of words
        private WhisperVadProcessor EnsureVadLoaded(SpeechOptions options)
        {
            if (_vad != null) return _vad;

            ChooseRuntime(options.UseGpu); // before Whisper.net loads anything, see the class summary
            long started = Stopwatch.GetTimestamp();
            try
            {
                // On the CPU: the model is tiny, and a GPU round trip would cost more than it saves
                _vadFactory = WhisperVadFactory.FromPath(VadModelPath, new WhisperFactoryOptions { UseGpu = false });
                _vad = _vadFactory.CreateBuilder()
                    .WithUseGpu(false)
                    .WithThreads(ThreadCount(WhisperThreads.MaxForVad))
                    .WithThreshold(0.3f)
                    .WithMinSpeechDuration(TimeSpan.FromMilliseconds(100))
                    .WithMinSilenceDuration(TimeSpan.FromMilliseconds(450))
                    .WithSpeechPadding(TimeSpan.FromMilliseconds(450))
                    .Build();
            }
            catch
            {
                FreeVad();
                throw;
            }
            AppLog.Info($"Voice activity detection loaded in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");
            return _vad;
        }

        private void EnsureModelLoaded(SpeechOptions options)
        {
            // Always first: with the GPU off too, Whisper.net must get the forced runtime (and the native log) before it
            // loads anything
            RuntimeLibrary runtime = ChooseRuntime(options.UseGpu);
            bool onGpu = options.UseGpu && runtime == RuntimeLibrary.Vulkan;
            // whisper.cpp would take the first card Vulkan lists, on a laptop often the integrated one
            GpuDevice? gpu = onGpu ? GpuChoice.Pick(VulkanDevices.List(), options.GpuName) : null;
            if (_factory != null && _loadedModel == options.ModelPath && _loadedOnGpu == onGpu && _loadedGpu == gpu) return;

            // Never two models in memory at once: they take hundreds of megabytes each
            Free();
            switch (SpeechModelFile.Check(options.ModelPath))
            {
                case SpeechModelFormat.Missing:
                    throw new SpeechException(SpeechError.ModelMissing, "The speech model is not on disk");
                case SpeechModelFormat.Ggml:
                    break;
                default:
                    throw new SpeechException(SpeechError.ModelUnsupported, "The speech model is not a whisper.cpp (ggml) file");
            }

            long started = Stopwatch.GetTimestamp();
            try
            {
                // Flash attention: faster, and less memory on the graphics card
                _factory = WhisperFactory.FromPath(
                    options.ModelPath,
                    new WhisperFactoryOptions { UseGpu = onGpu, GpuDevice = gpu?.Position ?? 0, UseFlashAttention = true });
                _factory.CreateBuilder(); // Whisper.net loads the model on first use: now, so that a bad file fails here
            }
            catch (WhisperModelLoadException ex)
            {
                Free();
                throw new SpeechException(SpeechError.ModelUnsupported, ex.Message, ex);
            }
            bool beamSearch = GpuChoice.UseBeamSearch(gpu);
            if (gpu != null && options.GpuName != null && gpu.Name != options.GpuName)
                AppLog.Info($"Graphics card {options.GpuName} not found, {gpu.Name} instead");
            string device = !onGpu ? "CPU" : $"GPU: {gpu?.Name ?? "none listed"}, {(beamSearch ? "beam search" : "greedy")}";
            AppLog.Info($"Speech model {Path.GetFileName(options.ModelPath)} loaded in " +
                $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s ({device})");

            // One state for every phrase: Whisper.net would create one, and its buffers, on each transcription
            started = Stopwatch.GetTimestamp();
            try
            {
                _context = ContextOf(_factory).Value;
                _state = WhisperNative.InitState(_context);
                if (_state == IntPtr.Zero)
                    throw new InvalidOperationException("whisper.cpp could not create a state for the speech model");
            }
            catch
            {
                Free();
                throw;
            }
            StatesCreated++;
            AppLog.Info($"Speech state ready in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");

            _loadedModel = options.ModelPath;
            _loadedOnGpu = onGpu;
            _loadedGpu = gpu;
            _beamSearch = beamSearch;
        }

        // Whisper.net keeps the model's whisper_context to itself (WhisperFactory.contextLazy in 1.9.2-preview1); a
        // renamed field fails here with MissingFieldException, which the smoke test reports
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "contextLazy")]
        private static extern ref Lazy<IntPtr> ContextOf(WhisperFactory factory);

        private void Free()
        {
            // The state before the model it belongs to
            if (_state != IntPtr.Zero) WhisperNative.FreeState(_state);
            _state = IntPtr.Zero;
            _context = IntPtr.Zero;
            try
            {
                _factory?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not free the speech model", ex);
            }
            _factory = null;
            _loadedModel = null;
            _warmedUp = false;
        }

        private void FreeVad()
        {
            try
            {
                _vad?.Dispose();
                _vadFactory?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not free the voice activity detector", ex);
            }
            _vad = null;
            _vadFactory = null;
        }

        private static RuntimeLibrary ChooseRuntime(bool useGpu)
        {
            lock (RuntimeLock)
            {
                if (_runtime is { } chosen) return chosen;

                bool arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
                string architecture = arm64 ? "arm64" : "x64";
                string runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
                string cpuFolder = Path.Combine(runtimes, "win-" + architecture);
                string vulkanFolder = Path.Combine(runtimes, "vulkan", "win-" + architecture);

                // ggml's CPU code (in both runtimes) needs AVX2 on x64 and kills the process with an illegal
                // instruction without it
                if (!arm64 && !CpuSupportsWhisper())
                    throw new SpeechException(SpeechError.CpuUnsupported, "The processor lacks AVX2, FMA or F16C");

                RuntimeLibrary runtime = RuntimeLibrary.Cpu;
                string folder = cpuFolder;
                if (useGpu) VulkanDevices.DisableImplicitLayers();
                // Vulkan comes with the graphics driver (vulkan-1.dll). No fallback after trying it: a half-loaded runtime
                // cannot be mixed with the other one's identically named DLLs.
                if (useGpu && !arm64 && File.Exists(Path.Combine(vulkanFolder, "ggml-vulkan-whisper.dll"))
                    && NativeLibrary.TryLoad("vulkan-1.dll", out _))
                {
                    runtime = RuntimeLibrary.Vulkan;
                    folder = vulkanFolder;
                }

                if (!File.Exists(Path.Combine(folder, "whisper.dll")))
                    throw new SpeechException(SpeechError.RuntimeMissing, $"{Path.Combine(folder, "whisper.dll")} is missing");
                foreach (string file in RuntimeFiles)
                {
                    string path = Path.Combine(folder, file);
                    if (!File.Exists(path)) continue;
                    // The VC++ runtime DLLs next to them are found because the path is absolute
                    if (!NativeLibrary.TryLoad(path, out IntPtr library))
                        throw new SpeechException(SpeechError.RuntimeMissing, $"{file} could not be loaded (missing VC++ runtime?)");
                    // The module Whisper.net loads from this same path: transcription calls into it directly
                    if (file == "whisper.dll") WhisperNative.Bind(library);
                }

                RuntimeOptions.ForcedRuntimeLibrary = runtime;
                _nativeLog ??= LogProvider.AddLogger(OnNativeLog);
                _runtime = runtime;
                AppLog.Info($"Speech recognition runtime: {runtime}, {ThreadCount(WhisperThreads.Max)} threads " +
                    $"({PhysicalCores.Value} cores, {Environment.ProcessorCount} logical processors)");
                return runtime;
            }
        }

        private static int ThreadCount(int max) => WhisperThreads.For(PhysicalCores.Value, Environment.ProcessorCount, max);

        private static unsafe int CountPhysicalCores()
        {
            // The first call tells the size the records need
            uint length = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, null, &length);
            var records = new byte[length];
            fixed (byte* buffer = records)
            {
                if (length == 0 || !GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, &length))
                {
                    // As before: a thread per logical processor
                    AppLog.Error($"Could not count the processor cores (error {Marshal.GetLastPInvokeError()})");
                    return 0;
                }
            }
            return WhisperThreads.CountCores(records.AsSpan(0, (int)length));
        }

        /// <summary>
        /// Under Native AOT, Avx2.IsSupported reflects the compile-time baseline instead of this processor, so ask
        /// CPUID, and Windows whether it saves the AVX registers on a context switch.
        /// </summary>
        private static bool CpuSupportsWhisper()
        {
            (int maxLeaf, _, _, _) = X86Base.CpuId(0, 0);
            if (maxLeaf < 7) return false;

            (_, _, int ecx, _) = X86Base.CpuId(1, 0);
            (_, int ebx7, _, _) = X86Base.CpuId(7, 0);
            bool fma = (ecx & (1 << 12)) != 0;
            bool osxsave = (ecx & (1 << 27)) != 0;
            bool avx = (ecx & (1 << 28)) != 0;
            bool f16c = (ecx & (1 << 29)) != 0;
            bool avx2 = (ebx7 & (1 << 5)) != 0;
            return fma && avx && f16c && avx2 && osxsave && (GetEnabledXStateFeatures() & XSTATE_MASK_AVX) != 0;
        }

        // Warnings and errors help when a model does not load; the device and its buffers tell why recognition is slow
        // on a given computer (ggml names the Vulkan device at Debug level). Loading details are left out, and
        // whisper.cpp never logs the recognized text unless asked to.
        private static void OnNativeLog(WhisperLogLevel level, string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            bool problem = level is WhisperLogLevel.Error or WhisperLogLevel.Warning;
            bool device = level is WhisperLogLevel.Info or WhisperLogLevel.Debug
                && (message.Contains("vulkan", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("backend", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("buffer", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("flash", StringComparison.OrdinalIgnoreCase));
            if (!problem && !device) return;

            // whisper.cpp describes the backend and its buffers again for every transcription: once is enough
            string line = message.Trim();
            lock (LoggedNativeMessages)
            {
                if (!LoggedNativeMessages.Add(line)) return;
            }
            AppLog.Info("whisper.cpp: " + line);
        }

        public void Dispose()
        {
            // A transcription still running uses the model: freeing it underneath would crash
            if (!_lock.Wait(TimeSpan.FromSeconds(3))) return;
            Free();
            FreeVad();
            _lock.Release();
        }

        // The XSAVE features Windows enables (Windows 7 SP1 and later)
        [LibraryImport("kernel32.dll")]
        private static partial ulong GetEnabledXStateFeatures();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool GetLogicalProcessorInformationEx(int relationshipType, byte* buffer, uint* returnedLength);
    }
}
