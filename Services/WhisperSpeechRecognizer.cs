using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
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
    /// for good, so the runtime is chosen here, before Whisper.net is touched, and forced.
    /// </summary>
    public sealed partial class WhisperSpeechRecognizer : ISpeechRecognizer
    {
        private const int PF_AVX2_INSTRUCTIONS_AVAILABLE = 40;

        // Load order of a runtime folder: dependencies first, as Whisper.net does it
        private static readonly string[] RuntimeFiles =
        {
            "ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-vulkan-whisper.dll", "ggml-whisper.dll", "whisper.dll",
        };

        private static readonly object RuntimeLock = new();
        private static RuntimeLibrary? _runtime;
        private static IDisposable? _nativeLog;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private WhisperFactory? _factory;
        private WhisperProcessor? _processor;
        private string? _loadedModel;
        private bool _loadedOnGpu;
        private string? _language;

        public Task PreloadAsync(SpeechOptions options, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                await _lock.WaitAsync(cancellationToken);
                try
                {
                    EnsureLoaded(options);
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
                    WhisperProcessor processor = EnsureLoaded(options);
                    var segments = new List<string>();
                    await foreach (SegmentData segment in processor.ProcessAsync(samples, cancellationToken))
                        segments.Add(segment.Text);
                    return (IReadOnlyList<string>)segments;
                }
                finally
                {
                    _lock.Release();
                }
            }, cancellationToken);

        /// <summary>Frees the model unless it is in use right now (then the next idle period frees it).</summary>
        public void Unload()
        {
            if (!_lock.Wait(0)) return;
            try
            {
                Free();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Chooses and loads the native runtime, as the first dictation would, and describes it (for the smoke test).
        /// </summary>
        public static string LoadRuntime(bool useGpu)
        {
            RuntimeLibrary runtime = ChooseRuntime(useGpu);
            return $"{runtime}: {WhisperFactory.GetRuntimeInfo()?.Trim()}";
        }

        private WhisperProcessor EnsureLoaded(SpeechOptions options)
        {
            bool onGpu = options.UseGpu && ChooseRuntime(options.UseGpu) == RuntimeLibrary.Vulkan;

            if (_processor == null || _loadedModel != options.ModelPath || _loadedOnGpu != onGpu)
            {
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
                    _factory = WhisperFactory.FromPath(options.ModelPath, new WhisperFactoryOptions { UseGpu = onGpu });
                    _processor = _factory.CreateBuilder()
                        .WithLanguage(options.Language)
                        .WithNoContext()
                        .WithThreads(Math.Clamp(Environment.ProcessorCount, 1, 8))
                        .Build();
                }
                catch (WhisperModelLoadException ex)
                {
                    Free();
                    throw new SpeechException(SpeechError.ModelUnsupported, ex.Message, ex);
                }

                _loadedModel = options.ModelPath;
                _loadedOnGpu = onGpu;
                _language = options.Language;
                AppLog.Info($"Speech model {Path.GetFileName(options.ModelPath)} loaded in " +
                    $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s ({(onGpu ? "GPU" : "CPU")})");
            }
            else if (_language != options.Language)
            {
                _processor.ChangeLanguage(options.Language);
                _language = options.Language;
            }
            return _processor;
        }

        private void Free()
        {
            _processor?.Dispose();
            _processor = null;
            _factory?.Dispose();
            _factory = null;
            _loadedModel = null;
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
        /// Windows (which also checks that the OS saves the AVX registers) and CPUID.
        /// </summary>
        private static bool CpuSupportsWhisper()
        {
            if (!IsProcessorFeaturePresent(PF_AVX2_INSTRUCTIONS_AVAILABLE)) return false;

            (_, _, int ecx, _) = X86Base.CpuId(1, 0);
            bool fma = (ecx & (1 << 12)) != 0;
            bool f16c = (ecx & (1 << 29)) != 0;
            return fma && f16c;
        }

        // whisper.cpp reports loading details at Info level; warnings and errors help when a model does not load.
        // It never logs the recognized text unless asked to.
        private static void OnNativeLog(WhisperLogLevel level, string? message)
        {
            if (level is WhisperLogLevel.Error or WhisperLogLevel.Warning && !string.IsNullOrWhiteSpace(message))
                AppLog.Info("whisper.cpp: " + message.Trim());
        }

        public void Dispose()
        {
            // A transcription still running uses the model: freeing it underneath would crash
            if (!_lock.Wait(TimeSpan.FromSeconds(3))) return;
            Free();
        }

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsProcessorFeaturePresent(int processorFeature);
    }
}
