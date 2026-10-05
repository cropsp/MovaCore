using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using MovaCore.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MovaCore.Services
{
    /// <summary>
    /// Records a microphone through WASAPI in shared mode. The audio engine converts whatever the device delivers into
    /// 16 kHz mono float (AutoConvertPcm), so no resampling happens here; asking a driver for float without that
    /// conversion is what makes some (Realtek) devices deliver only zeros.
    /// </summary>
    public sealed class WasapiAudioRecorder : IAudioRecorder
    {
        private const int E_ACCESSDENIED = unchecked((int)0x80070005);
        private const int BufferMilliseconds = 100;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

        // A bit more than the orchestrator's time limit: a recording whose release was missed must not grow forever
        private const int MaxSamples = AudioSamples.SampleRate * 130;

        private static readonly WaveFormat CaptureFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioSamples.SampleRate, 1);

        private readonly object _lock = new();
        private float[] _buffer = new float[AudioSamples.SampleRate * 10]; // guarded by _lock
        private int _count; // guarded by _lock
        private WasapiRecorder? _recorder; // Start and Stop are called by one thread at a time
        private MMDevice? _device;
        private volatile float _level;
        private volatile Exception? _captureError; // why NAudio's capture thread ended, if it failed

        public bool IsRecording => _recorder != null;

        public float CurrentLevel => _level;

        public IReadOnlyList<AudioInputDevice> GetInputDevices()
        {
            var result = new List<AudioInputDevice>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (MMDevice device in devices)
                {
                    using (device) result.Add(new AudioInputDevice(device.ID, device.FriendlyName));
                }
            }
            catch (Exception ex)
            {
                // Without a microphone list the settings still open; only an unexpected kind of failure is a bug
                if (ex is COMException or UnauthorizedAccessException)
                    AppLog.Info($"Could not list the microphones: {ex.Message}");
                else
                    AppLog.Error("Could not list the microphones", ex);
            }
            return result;
        }

        public void Start(string? deviceId)
        {
            if (_recorder != null) throw new InvalidOperationException("Already recording");

            MMDevice device;
            try
            {
                device = OpenDevice(deviceId);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                throw ToSpeechException(ex);
            }

            WasapiRecorder? recorder = null;
            try
            {
                recorder = new WasapiRecorderBuilder()
                    .WithDevice(device)
                    .WithSharedMode()
                    .WithFormat(CaptureFormat)
                    .WithBufferLength(BufferMilliseconds)
                    .Build();
                recorder.DataAvailable += OnDataAvailable;
                recorder.RecordingStopped += OnRecordingStopped;
                lock (_lock) _count = 0;
                _captureError = null;
                recorder.StartRecording();

                // NAudio's capture thread sets Capturing after IAudioClient.Start, overwriting a stop requested in
                // between, and then never ends: a quick release must not reach Stop before that
                SpinWait.SpinUntil(() => recorder.CaptureState != CaptureState.Starting, StartTimeout);
                if (recorder.CaptureState == CaptureState.Stopped)
                {
                    throw new COMException(_captureError?.Message ?? "The microphone did not start",
                        _captureError?.HResult ?? 0);
                }
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                recorder?.Dispose();
                device.Dispose();
                throw ToSpeechException(ex);
            }

            _recorder = recorder;
            _device = device;
        }

        public float[] Stop()
        {
            WasapiRecorder? recorder = _recorder;
            if (recorder == null) return Array.Empty<float>();

            _recorder = null;
            // Asked again until it has stopped (see Start), within a time limit: Dispose waits for the capture thread
            var watch = Stopwatch.StartNew();
            while (recorder.CaptureState != CaptureState.Stopped && watch.Elapsed < StopTimeout)
            {
                recorder.StopRecording();
                Thread.Sleep(5);
            }
            if (recorder.CaptureState == CaptureState.Stopped)
                recorder.Dispose(); // every buffer delivered before the stop is in now
            else
                AppLog.Error("The microphone did not stop in time"); // not disposed: that would wait forever
            _device?.Dispose();
            _device = null;
            _level = 0;

            lock (_lock)
            {
                float[] samples = _buffer.AsSpan(0, _count).ToArray();
                Array.Clear(_buffer, 0, _count);
                _count = 0;
                return samples;
            }
        }

        // Access denied: the Windows privacy settings do not let desktop apps use the microphone
        private static SpeechException ToSpeechException(Exception ex) =>
            ex.HResult == E_ACCESSDENIED || ex is UnauthorizedAccessException
                ? new SpeechException(SpeechError.MicrophoneBlocked, ex.Message, ex)
                : new SpeechException(SpeechError.MicrophoneUnavailable, ex.Message, ex);

        /// <summary>The chosen microphone, or the Windows default one if it is not set or not present right now.</summary>
        private static MMDevice OpenDevice(string? deviceId)
        {
            using var enumerator = new MMDeviceEnumerator();
            if (deviceId != null)
            {
                try
                {
                    MMDevice device = enumerator.GetDevice(deviceId);
                    if (device.State == DeviceState.Active) return device;
                    device.Dispose();
                }
                catch (COMException)
                {
                    // Removed since it was chosen
                }
                AppLog.Info("The chosen microphone is not available; using the default one");
            }

            if (enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out MMDevice? defaultDevice))
                return defaultDevice;
            throw new SpeechException(SpeechError.MicrophoneUnavailable, "There is no active microphone");
        }

        // Called on NAudio's capture thread
        private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(buffer);
            _level = AudioSamples.Peak(samples);

            lock (_lock)
            {
                int room = MaxSamples - _count;
                if (room <= 0) return;
                if (samples.Length > room) samples = samples[..room];

                if (_count + samples.Length > _buffer.Length)
                    Array.Resize(ref _buffer, Math.Min(MaxSamples, Math.Max(_buffer.Length * 2, _count + samples.Length)));
                samples.CopyTo(_buffer.AsSpan(_count));
                _count += samples.Length;
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            // E.g. the microphone was unplugged while recording; what was captured so far is still used
            if (e.Exception == null) return;
            _captureError = e.Exception;
            AppLog.Info($"The recording stopped early: {e.Exception.Message}");
        }

        public void Dispose()
        {
            if (_recorder != null) Array.Clear(Stop());
        }
    }
}
