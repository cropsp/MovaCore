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
    /// voice activity detection keeps the speech, and the recognized text is pasted into the focused application. The
    /// microphone stays open for a while after a dictation, so that the next one starts at once. Hotkey events arrive
    /// on the hook thread, which must not block, so every event becomes a command processed in order by one worker
    /// loop; transcription runs beside the loop so that a cancellation is handled at once.
    /// </summary>
    public sealed class SpeechOrchestrator : IDisposable
    {
        private readonly IAudioRecorder _recorder;
        private readonly ISpeechRecognizer _recognizer;
        private readonly ISpeechDetector? _detector;
        private readonly DictationContext? _context;
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
        private TimeSpan _held; // how long the hotkey was held, once it is released
        private bool _finishing; // released; the last moments are still being recorded
        private bool _ignoreNextRelease;
        private int _closeTicket; // identifies the pending close of the microphone; a press makes it stale
        private bool _detectorFailed;

        /// <param name="detector">Voice activity detection; without it, an energy threshold decides what is silence.</param>
        /// <param name="context">The previous dictation, which the next phrase may continue; without it, none does.</param>
        public SpeechOrchestrator(
            IAudioRecorder recorder, ISpeechRecognizer recognizer, TextPaster paster, ISpeechDetector? detector = null,
            DictationContext? context = null)
        {
            _recorder = recorder;
            _recognizer = recognizer;
            _paster = paster;
            _detector = detector;
            _context = context;
            _loop = Task.Run(RunAsync);
        }

        /// <summary>Raised on a worker thread.</summary>
        public event EventHandler<SpeechStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// The text has just been pasted (the paste keys went out), on a worker thread: the final <see cref="StateChanged"/>
        /// follows once the clipboard is back, a moment later.
        /// </summary>
        public event EventHandler? TextPasted;

        public SpeechState State => _state;

        /// <summary>
        /// While recording, the latest audio for the level meter (see <see cref="IAudioRecorder.CopyRecent"/>); otherwise
        /// nothing, and 0 is returned. Called from the UI thread.
        /// </summary>
        public int CopyRecentAudio(Span<float> destination)
        {
            if (_state == SpeechState.Recording) return _recorder.CopyRecent(destination);
            destination.Clear();
            return 0;
        }

        /// <summary>Shorter presses are taken as accidental and discarded.</summary>
        internal TimeSpan MinRecording { get; init; } = TimeSpan.FromSeconds(0.3);

        /// <summary>The hook misses the release of a key let go while a UAC prompt has the secure desktop.</summary>
        internal TimeSpan MaxRecording { get; init; } = TimeSpan.FromMinutes(2);

        internal static TimeSpan MinAudioLength { get; } = TimeSpan.FromSeconds(1.25);

        /// <summary>
        /// How long the microphone stays open after a dictation: the next one starts at once, with the moment before the
        /// press. Windows shows the microphone as in use meanwhile, and a Bluetooth headset stays in headset mode.
        /// </summary>
        internal TimeSpan KeepMicrophoneOpen { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Recording goes on this long after the release: the audio still on its way from the device, and the end of the
        /// last word, which people often say while letting go.
        /// </summary>
        internal TimeSpan TrailingAudio { get; init; } = TimeSpan.FromMilliseconds(150);

        /// <summary>A recording discarded after a hold this long is reported (no speech, no signal); a shorter one is not.</summary>
        internal TimeSpan NoticeableHold { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Applies the settings. With dictation on and the model on disk, the model is loaded right away and stays
        /// loaded, so that no dictation waits for it. With dictation off, or the model gone (deleted), its memory is
        /// freed.
        /// </summary>
        public void Configure(SpeechSettings settings)
        {
            _settings = settings;
            if (!settings.Enabled)
            {
                Cancel();
                Post(new CloseMicrophone(null));
                Post(new UnloadRequested());
            }
            else if (SpeechModelFile.Check(settings.ModelPath) == SpeechModelFormat.Ggml)
            {
                Post(new PreloadRequested(new SpeechOptions(settings.ModelPath!, settings.Language, settings.UseGpu, settings.FastRecognition)));
            }
            else
            {
                Post(new UnloadRequested());
            }
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
                case FinishRecording finish when finish.Generation == _generation && _state == SpeechState.Recording && _finishing:
                    OnFinish();
                    break;
                case CloseMicrophone close when _state == SpeechState.Idle && (close.Ticket == null || close.Ticket == _closeTicket):
                    CloseRecorderQuietly();
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
                case PreloadRequested preload:
                    _ = PreloadAsync(preload.Options, generation: null);
                    break;
                case UnloadRequested:
                    _ = UnloadAsync();
                    break;
            }
        }

        private void OnPressed()
        {
            // A press during transcription (or the last moments of a recording) is ignored, and so is its release
            if (_state == SpeechState.Transcribing || _finishing) _ignoreNextRelease = true;
            if (_state != SpeechState.Idle) return;
            _ignoreNextRelease = false;
            _closeTicket++; // the microphone is needed again

            SpeechSettings settings = _settings;
            if (!settings.Enabled) return;

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

            int generation = ++_generation;
            _options = new SpeechOptions(settings.ModelPath!, settings.Language, settings.UseGpu, settings.FastRecognition);
            _restoreClipboard = settings.RestoreClipboard;
            _recordingStarted = Stopwatch.GetTimestamp();
            _workCts?.Dispose();
            _workCts = new CancellationTokenSource();
            // Shown at once: opening the microphone can take a moment, and the user should see the press was taken
            SetState(new SpeechStateChangedEventArgs(SpeechState.Recording));

            try
            {
                _recorder.Start(settings.MicrophoneId);
            }
            catch (SpeechException ex)
            {
                Fail(ex);
                return;
            }
            AppLog.Info($"Dictation: the microphone was ready in {Stopwatch.GetElapsedTime(_recordingStarted).TotalMilliseconds:0} ms");

            // Normally loaded already (see Configure); otherwise it loads while the user speaks
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
            if (_state != SpeechState.Recording || _finishing || (limitGeneration != null && limitGeneration != _generation)) return;
            if (limitGeneration != null) AppLog.Info("Dictation: the recording reached its time limit");

            _held = Stopwatch.GetElapsedTime(_recordingStarted);
            _finishing = true;
            if (TrailingAudio <= TimeSpan.Zero)
            {
                OnFinish();
                return;
            }
            int generation = _generation;
            _ = Task.Delay(TrailingAudio, _workCts!.Token).ContinueWith(
                t => Post(new FinishRecording(generation)), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private void OnFinish()
        {
            _finishing = false;
            float[] samples = _recorder.Stop();
            TimeSpan audio = AudioSamples.Duration(samples.Length);

            if (_held < MinRecording)
            {
                Array.Clear(samples);
                Discard($"the hotkey was held for only {_held.TotalSeconds:0.00} s", SpeechOutcome.Discarded);
                return;
            }
            float peak = AudioSamples.Peak(samples);
            if (AudioSamples.IsAllZero(samples))
            {
                // Seen with drivers whose native format is captured wrongly: worth knowing when nothing ever works
                Array.Clear(samples);
                Discard($"the microphone delivered {audio.TotalSeconds:0.0} s of digital silence (all zeros)", SpeechOutcome.NoSignal);
                return;
            }
            if (peak < AudioSamples.SignalPeak)
            {
                Array.Clear(samples);
                Discard($"{audio.TotalSeconds:0.0} s of audio without a signal (peak {AudioSamples.ToDecibels(peak):0} dBFS)",
                    SpeechOutcome.NoSignal);
                return;
            }

            // Levels only, never the audio: they tell a quiet microphone from a problem elsewhere
            AppLog.Info($"Dictation: {audio.TotalSeconds:0.0} s recorded, peak {AudioSamples.ToDecibels(peak):0} dBFS, " +
                $"RMS {AudioSamples.ToDecibels(AudioSamples.Rms(samples)):0} dBFS");
            SetState(new SpeechStateChangedEventArgs(SpeechState.Transcribing));
            _ = TranscribeAsync(samples, _options!, _restoreClipboard, NoSpeechOutcome(), _generation, _workCts!.Token);
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

        /// <param name="generation">The dictation waiting for the model, or null for a load ahead of time.</param>
        private async Task PreloadAsync(SpeechOptions options, int? generation)
        {
            try
            {
                await _recognizer.PreloadAsync(options, _disposeCts.Token);
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (generation is int waiting)
            {
                Post(new PreloadFailed(waiting, ex));
            }
            catch (Exception ex)
            {
                // Reported when the user presses the hotkey: loading is tried again then
                Describe(ex);
            }
        }

        private async Task UnloadAsync()
        {
            try
            {
                await _recognizer.UnloadAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not free the speech model", ex);
            }
        }

        private async Task TranscribeAsync(float[] samples, SpeechOptions options, bool restoreClipboard,
            SpeechOutcome noSpeech, int generation, CancellationToken cancellationToken)
        {
            SpeechOutcome outcome;
            SpeechError? error = null;
            string? detail = null;
            try
            {
                float[]? speech = await KeepSpeechAsync(samples, options, cancellationToken);
                if (speech == null)
                {
                    outcome = noSpeech;
                }
                else
                {
                    long started = Stopwatch.GetTimestamp();
                    float[] audio = AudioSamples.PadToMinimum(speech, MinAudioLength);
                    IReadOnlyList<string> segments = await _recognizer.TranscribeAsync(audio, options, cancellationToken);
                    string text = TranscriptText.Clean(segments);

                    // Never log the text itself: it is what the user said. The parts and the characters before cleaning
                    // tell whether Whisper repeated itself.
                    int audioContext = options.FastRecognition ? WhisperAudioContext.For(audio.Length) : WhisperAudioContext.Full;
                    int recognized = 0;
                    foreach (string segment in segments) recognized += segment.Trim().Length;
                    AppLog.Info($"Dictation: {AudioSamples.Duration(speech.Length).TotalSeconds:0.0} s of speech transcribed in " +
                        $"{Stopwatch.GetElapsedTime(started).TotalSeconds:0.00} s (audio context {audioContext}), " +
                        $"{segments.Count} part(s), {text.Length} characters of {recognized}");
                    Array.Clear(speech);
                    Array.Clear(audio);

                    if (text.Length == 0)
                    {
                        outcome = SpeechOutcome.Discarded;
                    }
                    else
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // A phrase that continues the previous dictation gets a space, and no capital mid-sentence
                        text = TranscriptJoiner.Join(_context?.TextBefore(), text);
                        PasteResult result = await _paster.PasteAsync(
                            text, restoreClipboard, () => TextPasted?.Invoke(this, EventArgs.Empty));
                        if (result == PasteResult.Pasted)
                            _context?.Remember(text);
                        else
                            _context?.Forget();
                        (outcome, error) = result switch
                        {
                            PasteResult.Pasted => (SpeechOutcome.Pasted, (SpeechError?)null),
                            PasteResult.NotObserved => (SpeechOutcome.NotPasted, null),
                            _ => (SpeechOutcome.Failed, SpeechError.ClipboardFailed),
                        };
                        if (result != PasteResult.Pasted) AppLog.Info($"Dictation: paste result {result}");
                    }
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
            Array.Clear(samples); // the audio is not kept a moment longer than needed
            Post(new WorkDone(generation, outcome, error, detail));
        }

        /// <summary>
        /// The speech in the recording, with the silence around and between it removed, or null if there is none.
        /// Without a voice activity detector (or if it fails), an energy threshold decides, and the audio stays whole.
        /// </summary>
        private async Task<float[]?> KeepSpeechAsync(float[] samples, SpeechOptions options, CancellationToken cancellationToken)
        {
            IReadOnlyList<SpeechSegment>? segments = null;
            if (_detector != null && !_detectorFailed)
            {
                try
                {
                    segments = await _detector.DetectSpeechAsync(samples, options, cancellationToken);
                }
                catch (SpeechException ex)
                {
                    // The recognizer reports the same problem (e.g. a missing runtime) in a moment
                    AppLog.Info($"Dictation: voice activity detection is not available ({ex.Error})");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Once: every dictation would fail the same way
                    _detectorFailed = true;
                    AppLog.Error("Voice activity detection failed; using an energy threshold from now on", ex);
                }
            }

            if (segments == null)
            {
                if (!AudioSamples.IsSilent(samples)) return samples;
                AppLog.Info("Dictation discarded: no frame was loud enough to be speech");
                return null;
            }
            if (segments.Count == 0)
            {
                AppLog.Info("Dictation discarded: voice activity detection found no speech");
                return null;
            }

            float[] speech = AudioSamples.KeepSegments(samples, segments);
            AppLog.Info($"Dictation: voice activity detection kept {AudioSamples.Duration(speech.Length).TotalSeconds:0.0} s " +
                $"in {segments.Count} segment(s)");
            return speech;
        }

        // Reported only after a deliberate hold: a short tap that caught nothing is not worth a message
        private SpeechOutcome NoSpeechOutcome() => _held >= NoticeableHold ? SpeechOutcome.NoSpeech : SpeechOutcome.Discarded;

        /// <param name="outcome">Discarded, or what to report after a deliberate hold.</param>
        private void Discard(string reason, SpeechOutcome outcome)
        {
            AppLog.Info($"Dictation discarded: {reason}");
            _workCts?.Cancel();
            SetIdle(outcome == SpeechOutcome.Discarded || _held >= NoticeableHold ? outcome : SpeechOutcome.Discarded);
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

        private void CloseRecorderQuietly()
        {
            if (!_recorder.IsOpen) return;
            try
            {
                _recorder.Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not close the microphone", ex);
            }
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
            if (_recorder.IsOpen) ScheduleClose();
        }

        private void ScheduleClose()
        {
            int ticket = ++_closeTicket;
            if (KeepMicrophoneOpen <= TimeSpan.Zero)
            {
                CloseRecorderQuietly();
                return;
            }
            _ = Task.Delay(KeepMicrophoneOpen, _disposeCts.Token).ContinueWith(
                t => Post(new CloseMicrophone(ticket)), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private void SetState(SpeechStateChangedEventArgs change)
        {
            if (change.State != SpeechState.Recording) _finishing = false;
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
            CloseRecorderQuietly();
        }

        private abstract record Command;
        private sealed record Pressed : Command;
        private sealed record Released(int? Generation) : Command;
        private sealed record FinishRecording(int Generation) : Command;
        private sealed record CloseMicrophone(int? Ticket) : Command; // null: now, whatever is pending
        private sealed record CancelRequested : Command;
        private sealed record PreloadFailed(int Generation, Exception Error) : Command;
        private sealed record WorkDone(int Generation, SpeechOutcome Outcome, SpeechError? Error, string? Detail) : Command;
        private sealed record PreloadRequested(SpeechOptions Options) : Command;
        private sealed record UnloadRequested : Command;
    }
}
