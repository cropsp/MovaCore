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
    /// conversion is what makes some (Realtek) devices deliver only zeros. While the microphone is open, the last half
    /// second is kept in a ring: the start of the next recording and the level meter come from it.
    /// </summary>
    public sealed class WasapiAudioRecorder : IAudioRecorder
    {
        private const int E_ACCESSDENIED = unchecked((int)0x80070005);
        private const int BufferMilliseconds = 100;
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

        // A bit more than the orchestrator's time limit: a recording whose release was missed must not grow forever
        private const int MaxSamples = AudioSamples.SampleRate * 130;

        // Audio before the press that a recording on an open microphone starts with
        private const int PreRollSamples = AudioSamples.SampleRate * 3 / 10;
        private const int RingSamples = AudioSamples.SampleRate / 2;

        private static readonly WaveFormat CaptureFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioSamples.SampleRate, 1);

        private readonly object _lock = new();
        private float[] _buffer = new float[AudioSamples.SampleRate * 10]; // guarded by _lock
        private int _count; // guarded by _lock
        private readonly float[] _ring = new float[RingSamples]; // guarded by _lock
        private int _ringEnd; // where the next sample goes; guarded by _lock
        private int _ringCount; // guarded by _lock
        private volatile bool _recording; // written under _lock
        private WasapiRecorder? _recorder; // Start, Stop and Close are called by one thread at a time
        private MMDevice? _device;
        private string? _endpointId; // the open microphone's
        private volatile Exception? _captureError; // why NAudio's capture thread ended, if it failed

        public bool IsOpen => _recorder != null;

        public bool IsRecording => _recording;

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
            if (_recording) throw new InvalidOperationException("Already recording");

            if (_recorder != null && !IsOpenOn(deviceId)) Close();
            if (_recorder == null) Open(deviceId);

            lock (_lock)
            {
                // On a microphone that was already open, people who start speaking as they press lose nothing
                int preRoll = Math.Min(_ringCount, PreRollSamples);
                CopyFromRing(_buffer.AsSpan(0, preRoll));
                _count = preRoll;
                _recording = true;
            }
        }

        public float[] Stop()
        {
            float[] samples;
            lock (_lock)
            {
                if (!_recording) return Array.Empty<float>();
                _recording = false;
                samples = _buffer.AsSpan(0, _count).ToArray();
                Array.Clear(_buffer, 0, _count);
                _count = 0;
            }

            // A microphone that failed while recording (unplugged) is let go now rather than on the next press
            if (_recorder?.CaptureState == CaptureState.Stopped) Close();
            return samples;
        }

        public void Close()
        {
            WasapiRecorder? recorder = _recorder;
            if (recorder != null)
            {
                _recorder = null;
                // Asked again until it has stopped (see Open), within a time limit: Dispose waits for the capture thread
                var watch = Stopwatch.StartNew();
                while (recorder.CaptureState != CaptureState.Stopped && watch.Elapsed < StopTimeout)
                {
                    recorder.StopRecording();
                    Thread.Sleep(5);
                }
                if (recorder.CaptureState == CaptureState.Stopped)
                {
                    recorder.Dispose();
                }
                else
                {
                    // Not disposed: that would wait forever. Whatever it still captures must not reach the next stream.
                    recorder.DataAvailable -= OnDataAvailable;
                    AppLog.Error("The microphone did not stop in time");
                }
            }
            _device?.Dispose();
            _device = null;
            _endpointId = null;

            lock (_lock)
            {
                _recording = false;
                Array.Clear(_buffer, 0, _count);
                _count = 0;
                Array.Clear(_ring);
                _ringEnd = 0;
                _ringCount = 0;
            }
        }

        public int CopyRecent(Span<float> destination)
        {
            lock (_lock)
            {
                int count = Math.Min(destination.Length, _ringCount);
                destination[..^count].Clear();
                CopyFromRing(destination[^count..]);
                return count;
            }
        }

        private void Open(string? deviceId)
        {
            MMDevice device;
            try
            {
                device = OpenDevice(deviceId, logFallback: true);
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
                _captureError = null;
                recorder.StartRecording();

                // NAudio's capture thread sets Capturing after IAudioClient.Start, overwriting a stop requested in
                // between, and then never ends: a quick release must not reach Close before that
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
            _endpointId = device.ID;
        }

        /// <summary>
        /// Whether the open microphone is still capturing and is the one a recording with this device ID would use: the
        /// user may have chosen another one, or changed the Windows default.
        /// </summary>
        private bool IsOpenOn(string? deviceId)
        {
            if (_recorder?.CaptureState != CaptureState.Capturing) return false;
            try
            {
                using MMDevice device = OpenDevice(deviceId, logFallback: false);
                return device.ID == _endpointId;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or SpeechException)
            {
                return false;
            }
        }

        // The newest destination.Length samples of the ring, oldest first; the caller holds _lock
        private void CopyFromRing(Span<float> destination)
        {
            int start = (_ringEnd - destination.Length + RingSamples) % RingSamples;
            int first = Math.Min(destination.Length, RingSamples - start);
            _ring.AsSpan(start, first).CopyTo(destination);
            _ring.AsSpan(0, destination.Length - first).CopyTo(destination[first..]);
        }

        // Access denied: the Windows privacy settings do not let desktop apps use the microphone
        private static SpeechException ToSpeechException(Exception ex) =>
            ex.HResult == E_ACCESSDENIED || ex is UnauthorizedAccessException
                ? new SpeechException(SpeechError.MicrophoneBlocked, ex.Message, ex)
                : new SpeechException(SpeechError.MicrophoneUnavailable, ex.Message, ex);

        /// <summary>The chosen microphone, or the Windows default one if it is not set or not present right now.</summary>
        private static MMDevice OpenDevice(string? deviceId, bool logFallback)
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
                if (logFallback) AppLog.Info("The chosen microphone is not available; using the default one");
            }

            if (enumerator.TryGetDefaultAudioEndpoint(DataFlow.Capture, Role.Console, out MMDevice? defaultDevice))
                return defaultDevice;
            throw new SpeechException(SpeechError.MicrophoneUnavailable, "There is no active microphone");
        }

        // Called on NAudio's capture thread
        private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(buffer);

            lock (_lock)
            {
                ReadOnlySpan<float> recent = samples.Length > RingSamples ? samples[^RingSamples..] : samples;
                int first = Math.Min(recent.Length, RingSamples - _ringEnd);
                recent[..first].CopyTo(_ring.AsSpan(_ringEnd));
                recent[first..].CopyTo(_ring);
                _ringEnd = (_ringEnd + recent.Length) % RingSamples;
                _ringCount = Math.Min(RingSamples, _ringCount + recent.Length);

                if (!_recording) return;
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

        public void Dispose() => Close();
    }
}
