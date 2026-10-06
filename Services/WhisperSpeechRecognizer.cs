using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// for good, so the runtime is chosen here, before Whisper.net is touched, and forced. Voice activity detection
    /// (whisper.cpp's Silero VAD, model in models\ next to the exe) lives here too, since it needs that same runtime.
    /// </summary>
    public sealed partial class WhisperSpeechRecognizer : ISpeechRecognizer, ISpeechDetector
    {
        /// <summary>The Silero VAD model, shipped in models\ next to the exe.</summary>
        public const string VadModelFileName = "ggml-silero-v6.2.0.bin";

        private const ulong XSTATE_MASK_AVX = 1UL << 2;

        // Load order of a runtime folder: dependencies first, as Whisper.net does it
        private static readonly string[] RuntimeFiles =
        {
            "ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-vulkan-whisper.dll", "ggml-whisper.dll", "whisper.dll",
        };

        private static readonly object RuntimeLock = new();
        private static readonly HashSet<string> LoggedNativeMessages = new(StringComparer.Ordinal);
        private static RuntimeLibrary? _runtime;
        private static IDisposable? _nativeLog;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private int _processorContext;
        private string? _loadedModel;
        private bool _loadedOnGpu;
        private bool _warmedUp;
        private string? _language;
        private WhisperVadFactory? _vadFactory;
        private WhisperVadProcessor? _vad;

        public static string VadModelPath { get; } = Path.Combine(AppContext.BaseDirectory, "models", VadModelFileName);

        public Task PreloadAsync(SpeechOptions options, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    // The processor short phrases need, or the full one
                    WhisperProcessor processor = GetProcessor(
                        options, options.FastRecognition ? WhisperAudioContext.Step : WhisperAudioContext.Full);
                    if (!_warmedUp)
                    {
                        _warmedUp = true;
                        await WarmUpAsync(processor, cancellationToken);
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
                    WhisperProcessor processor = GetProcessor(options, audioContext);
                    var segments = new List<string>();
                    // Unlike ProcessAsync, which gives up on cancellation while whisper.cpp still runs, this completes
                    // only after the native call has returned: the lock must cover it, or the model could be freed
                    // underneath it
                    await processor.ProcessWithUtf8HandlerAsync(
                        samples, segment => segments.Add(Encoding.UTF8.GetString(segment.TextUtf8)), cancellationToken);
                    return (IReadOnlyList<string>)segments;
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
        private async Task WarmUpAsync(WhisperProcessor processor, CancellationToken cancellationToken)
        {
            if (!_loadedOnGpu) return;
            long started = Stopwatch.GetTimestamp();
            var silence = new float[AudioSamples.SampleRate * 5 / 4];
            await processor.ProcessWithUtf8HandlerAsync(silence, _ => { }, cancellationToken);
            AppLog.Info($"Speech model warmed up in {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");
        }

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
                    .WithThreads(Math.Clamp(Environment.ProcessorCount, 1, 4))
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
            if (_factory != null && _loadedModel == options.ModelPath && _loadedOnGpu == onGpu) return;

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
                    options.ModelPath, new WhisperFactoryOptions { UseGpu = onGpu, UseFlashAttention = true });
                _factory.CreateBuilder(); // Whisper.net loads the model on first use: now, so that a bad file fails here
            }
            catch (WhisperModelLoadException ex)
            {
                Free();
                throw new SpeechException(SpeechError.ModelUnsupported, ex.Message, ex);
            }

            _loadedModel = options.ModelPath;
            _loadedOnGpu = onGpu;
            AppLog.Info($"Speech model {Path.GetFileName(options.ModelPath)} loaded in " +
                $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s ({(onGpu ? "GPU" : "CPU")})");
        }

        /// <summary>
        /// A processor encodes a fixed audio context (<see cref="WhisperAudioContext"/>), so another one is built when a
        /// phrase needs a different one. One at a time: each holds buffers of its own, on the graphics card too.
        /// </summary>
        private WhisperProcessor GetProcessor(SpeechOptions options, int audioContext)
        {
            EnsureModelLoaded(options);
            if (_processor != null && _processorContext == audioContext)
            {
                if (_language != options.Language)
                {
                    _processor.ChangeLanguage(options.Language);
                    _language = options.Language;
                }
                return _processor;
            }

            FreeProcessor();
            long started = Stopwatch.GetTimestamp();
            WhisperProcessorBuilder builder = _factory!.CreateBuilder()
                .WithLanguage(options.Language)
                .WithNoContext()
                .WithThreads(Math.Clamp(Environment.ProcessorCount, 1, 8));
            if (audioContext < WhisperAudioContext.Full)
            {
                // A short context makes Whisper prone to repeating the phrase once it is done: one segment, and a
                // ceiling of ~15 tokens a second, far above speech, stop that loop (TranscriptText drops what is left)
                builder.WithAudioContextSize(audioContext)
                    .WithSingleSegment()
                    .WithMaxTokensPerSegment(audioContext / 4);
            }
            _processor = builder.Build();
            _processorContext = audioContext;
            _language = options.Language;
            AppLog.Info($"Speech processor for audio context {audioContext} ready in " +
                $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms");
            return _processor;
        }

        private void FreeProcessor()
        {
            try
            {
                _processor?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not free the speech processor", ex);
            }
            _processor = null;
            _processorContext = 0;
        }

        private void Free()
        {
            FreeProcessor();
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
                // Implicit Vulkan layers (the overlays of OBS, Steam, RTSS and the like) load into every Vulkan process
                // and have crashed speech recognition in other apps (Handy); a value the user set is kept
                if (useGpu && Environment.GetEnvironmentVariable("VK_LOADER_LAYERS_DISABLE") == null)
                    Environment.SetEnvironmentVariable("VK_LOADER_LAYERS_DISABLE", "~implicit~");
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
                    // The VC++ runtime DLLs next to them are found because the path is absolute
                    if (File.Exists(path) && !NativeLibrary.TryLoad(path, out _))
                        throw new SpeechException(SpeechError.RuntimeMissing, $"{file} could not be loaded (missing VC++ runtime?)");
                }

                RuntimeOptions.ForcedRuntimeLibrary = runtime;
                _nativeLog ??= LogProvider.AddLogger(OnNativeLog);
                _runtime = runtime;
                AppLog.Info($"Speech recognition runtime: {runtime}");
                return runtime;
            }
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
    }
}
