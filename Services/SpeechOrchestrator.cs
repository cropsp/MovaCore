using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Hold-to-talk dictation: the hotkey press starts recording (and loads the model meanwhile), the release stops it,
    /// and the recognized text is pasted into the focused application. Hotkey events arrive on the hook thread, which
    /// must not block, so every event becomes a command processed in order by one worker loop; transcription runs
    /// beside the loop so that a cancellation is handled at once.
    /// </summary>
    public sealed class SpeechOrchestrator : IDisposable
    {
        private readonly IAudioRecorder _recorder;
        private readonly ISpeechRecognizer _recognizer;
        private readonly TextPaster _paster;
        private readonly Channel<Command> _commands =
            Channel.CreateUnbounded<Command>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource _disposeCts = new();
        private readonly Task _loop;
        private volatile SpeechSettings _settings = SpeechSettings.Disabled;
        private volatile SpeechState _state = SpeechState.Idle;

        // Owned by the loop
        private int _generation; // identifies the current dictation; a cancelled one's late results are ignored
        private CancellationTokenSource? _workCts; // the current dictation's time limit and transcription
        private SpeechOptions? _options;
        private bool _restoreClipboard;
        private long _recordingStarted;
        private bool _ignoreNextRelease;
        private int _idleVersion;

        public SpeechOrchestrator(IAudioRecorder recorder, ISpeechRecognizer recognizer, TextPaster paster)
        {
            _recorder = recorder;
            _recognizer = recognizer;
            _paster = paster;
            _loop = Task.Run(RunAsync);
        }

        /// <summary>Raised on a worker thread.</summary>
        public event EventHandler<SpeechStateChangedEventArgs>? StateChanged;

        public SpeechState State => _state;

        /// <summary>The microphone level while recording, from 0 to 1.</summary>
        public float CurrentLevel => _state == SpeechState.Recording ? _recorder.CurrentLevel : 0;

        /// <summary>Shorter presses are taken as accidental and discarded.</summary>
        internal TimeSpan MinRecording { get; init; } = TimeSpan.FromSeconds(0.3);

        /// <summary>The hook misses the release of a key let go while a UAC prompt has the secure desktop.</summary>
        internal TimeSpan MaxRecording { get; init; } = TimeSpan.FromMinutes(2);

        /// <summary>The model takes hundreds of megabytes; it is freed after this long without dictation.</summary>
        internal TimeSpan IdleUnload { get; init; } = TimeSpan.FromMinutes(10);

        internal static TimeSpan MinAudioLength { get; } = TimeSpan.FromSeconds(1.25);

        public void Configure(SpeechSettings settings)
        {
            _settings = settings;
            if (!settings.Enabled) Cancel();
        }

        /// <summary>Called on the hook thread: never blocks.</summary>
        public void OnHotkeyPressed() => Post(new Pressed());

        /// <summary>Called on the hook thread: never blocks.</summary>
        public void OnHotkeyReleased() => Post(new Released(null));

        /// <summary>Stops a recording or transcription in progress without pasting anything.</summary>
        public void Cancel() => Post(new CancelRequested());

        private void Post(Command command) => _commands.Writer.TryWrite(command);

        private async Task RunAsync()
        {
            await foreach (Command command in _commands.Reader.ReadAllAsync())
            {
                try
                {
                    Handle(command);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Dictation failed", ex);
                    if (_recorder.IsRecording) StopRecorderQuietly();
                    _workCts?.Cancel();
                    _generation++;
                    SetIdle(SpeechOutcome.Failed, SpeechError.Failed, ex.Message);
                }
            }
        }

        private void Handle(Command command)
        {
            switch (command)
            {
                case Pressed:
                    OnPressed();
                    break;
                case Released released:
                    OnReleased(released.Generation);
                    break;
                case CancelRequested:
                    OnCancel();
                    break;
                case PreloadFailed failed when failed.Generation == _generation && _state == SpeechState.Recording:
                    StopRecorderQuietly();
                    Fail(failed.Error);
                    break;
                case WorkDone done when done.Generation == _generation && _state == SpeechState.Transcribing:
                    SetIdle(done.Outcome, done.Error, done.Detail);
                    break;
                case IdleTimeout idle when idle.Version == _idleVersion && _state == SpeechState.Idle:
                    AppLog.Info("Dictation: the speech model is unloaded after a while without use");
                    _recognizer.Unload();
                    break;
            }
        }

        private void OnPressed()
        {
            // A press during transcription is ignored, and so is its release
            if (_state == SpeechState.Transcribing) _ignoreNextRelease = true;
            if (_state != SpeechState.Idle) return;
            _ignoreNextRelease = false;

            SpeechSettings settings = _settings;
            if (!settings.Enabled) return;
            _idleVersion++; // the model stays loaded while it is in use

            switch (SpeechModelFile.Check(settings.ModelPath))
            {
                case SpeechModelFormat.Missing:
                    Fail(new SpeechException(SpeechError.ModelMissing, "The speech model is not on disk"));
                    return;
                case SpeechModelFormat.Gguf:
                case SpeechModelFormat.Unknown:
                    Fail(new SpeechException(SpeechError.ModelUnsupported, "The speech model is not a whisper.cpp (ggml) file"));
                    return;
            }

            try
            {
                _recorder.Start(settings.MicrophoneId);
            }
            catch (SpeechException ex)
            {
                Fail(ex);
                return;
            }

            int generation = ++_generation;
            _options = new SpeechOptions(settings.ModelPath!, settings.Language, settings.UseGpu);
            _restoreClipboard = settings.RestoreClipboard;
            _recordingStarted = Stopwatch.GetTimestamp();
            _workCts?.Dispose();
            _workCts = new CancellationTokenSource();
            SetState(new SpeechStateChangedEventArgs(SpeechState.Recording));

            // The model loads while the user speaks
            _ = PreloadAsync(_options, generation);
            _ = Task.Delay(MaxRecording, _workCts.Token).ContinueWith(
                t => Post(new Released(generation)), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        /// <param name="limitGeneration">Set when the time limit ended the recording, null for the hotkey release.</param>
        private void OnReleased(int? limitGeneration)
        {
            if (limitGeneration == null && _ignoreNextRelease)
            {
                _ignoreNextRelease = false;
                return;
            }
            if (_state != SpeechState.Recording || (limitGeneration != null && limitGeneration != _generation)) return;
            if (limitGeneration != null) AppLog.Info("Dictation: the recording reached its time limit");

            float[] samples = _recorder.Stop();
            TimeSpan held = Stopwatch.GetElapsedTime(_recordingStarted);
            TimeSpan audio = AudioSamples.Duration(samples.Length);

            if (held < MinRecording)
            {
                Discard($"the hotkey was held for only {held.TotalSeconds:0.00} s");
                return;
            }
            if (AudioSamples.IsAllZero(samples))
            {
                // Seen with drivers whose native format is captured wrongly: worth knowing when nothing ever works
                Discard($"the microphone delivered {audio.TotalSeconds:0.0} s of digital silence (all zeros)");
                return;
            }
            if (AudioSamples.IsSilent(samples))
            {
                Discard($"{audio.TotalSeconds:0.0} s of audio without speech (peak {AudioSamples.Peak(samples):0.000})");
                return;
            }

            SetState(new SpeechStateChangedEventArgs(SpeechState.Transcribing));
            _ = TranscribeAsync(samples, _options!, _restoreClipboard, _generation, _workCts!.Token);
        }

        private void OnCancel()
        {
            if (_state == SpeechState.Idle) return;

            if (_state == SpeechState.Recording) StopRecorderQuietly();
            _workCts?.Cancel();
            _generation++;
            AppLog.Info("Dictation cancelled");
            SetIdle(SpeechOutcome.Cancelled);
        }

        private async Task PreloadAsync(SpeechOptions options, int generation)
        {
            try
            {
                await _recognizer.PreloadAsync(options, _disposeCts.Token);
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Post(new PreloadFailed(generation, ex));
            }
        }

        private async Task TranscribeAsync(
            float[] samples, SpeechOptions options, bool restoreClipboard, int generation, CancellationToken cancellationToken)
        {
            SpeechOutcome outcome;
            SpeechError? error = null;
            string? detail = null;
            try
            {
                long started = Stopwatch.GetTimestamp();
                float[] audio = AudioSamples.PadToMinimum(samples, MinAudioLength);
                IReadOnlyList<string> segments = await _recognizer.TranscribeAsync(audio, options, cancellationToken);
                string text = TranscriptText.Clean(segments);

                // Never log the text itself: it is what the user said
                AppLog.Info($"Dictation: {AudioSamples.Duration(samples.Length).TotalSeconds:0.0} s of audio transcribed in " +
                    $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.00} s, {text.Length} characters");
                Array.Clear(samples);
                Array.Clear(audio);

                if (text.Length == 0)
                {
                    outcome = SpeechOutcome.Discarded;
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PasteResult result = await _paster.PasteAsync(text, restoreClipboard);
                    (outcome, error) = result switch
                    {
                        PasteResult.Pasted => (SpeechOutcome.Pasted, (SpeechError?)null),
                        PasteResult.NotObserved => (SpeechOutcome.NotPasted, null),
                        _ => (SpeechOutcome.Failed, SpeechError.ClipboardFailed),
                    };
                    if (result != PasteResult.Pasted) AppLog.Info($"Dictation: paste result {result}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = SpeechOutcome.Cancelled;
            }
            catch (Exception ex)
            {
                (outcome, error, detail) = Describe(ex);
            }
            Post(new WorkDone(generation, outcome, error, detail));
        }

        private void Discard(string reason)
        {
            AppLog.Info($"Dictation discarded: {reason}");
            _workCts?.Cancel();
            SetIdle(SpeechOutcome.Discarded);
        }

        private void Fail(Exception ex)
        {
            (SpeechOutcome outcome, SpeechError? error, string? detail) = Describe(ex);
            _workCts?.Cancel();
            SetIdle(outcome, error, detail);
        }

        // Conditions the user can fix are logged as information; anything else is a bug
        private static (SpeechOutcome, SpeechError?, string?) Describe(Exception ex)
        {
            if (ex is SpeechException speech)
            {
                AppLog.Info($"Dictation failed ({speech.Error}): {speech.Message}");
                return (SpeechOutcome.Failed, speech.Error, speech.Message);
            }
            AppLog.Error("Dictation failed", ex);
            return (SpeechOutcome.Failed, SpeechError.Failed, ex.Message);
        }

        private void StopRecorderQuietly()
        {
            try
            {
                Array.Clear(_recorder.Stop());
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not stop the recording", ex);
            }
        }

        private void SetIdle(SpeechOutcome outcome, SpeechError? error = null, string? detail = null)
        {
            SetState(new SpeechStateChangedEventArgs(SpeechState.Idle, outcome, error, detail));

            int version = ++_idleVersion;
            _ = Task.Delay(IdleUnload, _disposeCts.Token).ContinueWith(
                t => Post(new IdleTimeout(version)), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private void SetState(SpeechStateChangedEventArgs change)
        {
            _state = change.State;
            StateChanged?.Invoke(this, change);
        }

        public void Dispose()
        {
            _commands.Writer.TryComplete();
            _disposeCts.Cancel();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Errors inside the loop are logged there
            }
            _workCts?.Cancel();
            if (_recorder.IsRecording) StopRecorderQuietly();
        }

        private abstract record Command;
        private sealed record Pressed : Command;
        private sealed record Released(int? Generation) : Command;
        private sealed record CancelRequested : Command;
        private sealed record PreloadFailed(int Generation, Exception Error) : Command;
        private sealed record WorkDone(int Generation, SpeechOutcome Outcome, SpeechError? Error, string? Detail) : Command;
        private sealed record IdleTimeout(int Version) : Command;
    }
}
