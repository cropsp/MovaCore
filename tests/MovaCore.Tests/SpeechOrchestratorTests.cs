using System.Collections.Concurrent;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public sealed class SpeechOrchestratorTests : IDisposable
    {
        private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

        private readonly string _modelPath = Path.Combine(Path.GetTempPath(), $"movacore-speech-{Guid.NewGuid():N}.bin");
        private readonly FakeAudioRecorder _recorder = new();
        private readonly FakeSpeechRecognizer _recognizer = new();
        private readonly FakeClipboard _clipboard = new();
        private readonly FakeHotkeyService _hotkeys = new();
        private BlockingCollection<SpeechStateChangedEventArgs> _events = new();
        private SpeechOrchestrator _orchestrator;

        public SpeechOrchestratorTests()
        {
            File.WriteAllBytes(_modelPath, new byte[] { 0x6C, 0x6D, 0x67, 0x67, 0 });
            _orchestrator = Create();
        }

        public void Dispose()
        {
            _orchestrator.Dispose();
            File.Delete(_modelPath);
        }

        private SpeechOrchestrator Create(
            TimeSpan? minRecording = null, TimeSpan? maxRecording = null, string? modelPath = "", ISpeechDetector? detector = null,
            TimeSpan? keepMicrophoneOpen = null, TimeSpan? trailingAudio = null, TimeSpan? noticeableHold = null,
            DictationContext? context = null)
        {
            var paster = new TextPaster(_hotkeys, _clipboard, new ClipboardGate())
            {
                PasteTimeout = TimeSpan.FromMilliseconds(100),
                RestoreDelay = TimeSpan.Zero,
            };
            var orchestrator = new SpeechOrchestrator(_recorder, _recognizer, paster, detector, context)
            {
                MinRecording = minRecording ?? TimeSpan.Zero,
                MaxRecording = maxRecording ?? TimeSpan.FromMinutes(1),
                KeepMicrophoneOpen = keepMicrophoneOpen ?? TimeSpan.FromHours(1),
                TrailingAudio = trailingAudio ?? TimeSpan.Zero,
                NoticeableHold = noticeableHold ?? TimeSpan.FromHours(1),
            };
            orchestrator.StateChanged += (_, e) => _events.Add(e);
            orchestrator.Configure(Settings(modelPath == "" ? _modelPath : modelPath));
            return orchestrator;
        }

        private void Recreate(
            TimeSpan? minRecording = null, TimeSpan? maxRecording = null, string? modelPath = "", ISpeechDetector? detector = null,
            TimeSpan? keepMicrophoneOpen = null, TimeSpan? trailingAudio = null, TimeSpan? noticeableHold = null,
            DictationContext? context = null)
        {
            _orchestrator.Dispose();
            _events.Dispose();
            _events = new BlockingCollection<SpeechStateChangedEventArgs>();
            _orchestrator = Create(minRecording, maxRecording, modelPath, detector, keepMicrophoneOpen, trailingAudio, noticeableHold, context);
        }

        private static SpeechSettings Settings(string? modelPath) =>
            new(true, modelPath, "uk", false, "{mic-1}", true);

        private SpeechStateChangedEventArgs Next()
        {
            Assert.True(_events.TryTake(out var change, WaitLimit), "No state change arrived");
            return change!;
        }

        private SpeechStateChangedEventArgs NextIdle()
        {
            while (true)
            {
                SpeechStateChangedEventArgs change = Next();
                if (change.State == SpeechState.Idle) return change;
            }
        }

        private void Dictate()
        {
            _orchestrator.OnHotkeyPressed();
            _orchestrator.OnHotkeyReleased();
        }

        [Fact]
        public void Dictation_RecordsTranscribesAndPastes()
        {
            Dictate();

            Assert.Equal(SpeechState.Recording, Next().State);
            Assert.Equal(SpeechState.Transcribing, Next().State);
            SpeechStateChangedEventArgs done = Next();
            Assert.Equal(SpeechState.Idle, done.State);
            Assert.Equal(SpeechOutcome.Pasted, done.Outcome);

            Assert.Equal("Привіт, світе.", _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal(new string?[] { "{mic-1}" }, _recorder.StartedDevices);
            var expected = new SpeechOptions(_modelPath, "uk", false);
            Assert.NotEmpty(_recognizer.Preloads); // ahead of time (Configure) and again on the press
            Assert.All(_recognizer.Preloads, options => Assert.Equal(expected, options));
            Assert.Equal(expected, Assert.Single(_recognizer.Transcriptions));
        }

        [Fact]
        public void FastRecognition_ReachesTheRecognizer()
        {
            _orchestrator.Configure(Settings(_modelPath) with { FastRecognition = true });

            Dictate();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.True(Assert.Single(_recognizer.Transcriptions).FastRecognition);
        }

        [Fact]
        public void ShortPress_IsDiscarded()
        {
            Recreate(minRecording: TimeSpan.FromHours(1));

            Dictate();

            Assert.Equal(SpeechOutcome.Discarded, NextIdle().Outcome);
            Assert.Empty(_recognizer.Transcriptions);
            Assert.False(_recorder.IsRecording);
        }

        [Fact]
        public void SilentRecording_IsNotTranscribed()
        {
            _recorder.Recording = new float[16000];

            Dictate();

            Assert.Equal(SpeechOutcome.Discarded, NextIdle().Outcome);
            Assert.Empty(_recognizer.Transcriptions);
        }

        [Fact]
        public void ShortClip_IsPaddedForWhisper()
        {
            _recorder.Recording = FakeAudioRecorder.Speech(0.5);

            Dictate();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.Equal(20000, _recognizer.SampleCount);
        }

        [Fact]
        public void NothingRecognized_PastesNothing()
        {
            _recognizer.Segments = new[] { "[BLANK_AUDIO]" };

            Dictate();

            Assert.Equal(SpeechOutcome.Discarded, NextIdle().Outcome);
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public void PasteNotTaken_LeavesTheTextOnTheClipboard()
        {
            _clipboard.PasteObserved = false;

            Dictate();

            Assert.Equal(SpeechOutcome.NotPasted, NextIdle().Outcome);
            Assert.Equal("Привіт, світе.", _clipboard.Text);
        }

        [Fact]
        public void MissingModel_IsReportedWithoutRecording()
        {
            Recreate(modelPath: _modelPath + ".missing");

            _orchestrator.OnHotkeyPressed();

            SpeechStateChangedEventArgs change = NextIdle();
            Assert.Equal(SpeechOutcome.Failed, change.Outcome);
            Assert.Equal(SpeechError.ModelMissing, change.Error);
            Assert.Empty(_recorder.StartedDevices);
        }

        [Fact]
        public void NoModelChosen_IsReportedAsMissing()
        {
            Recreate(modelPath: null);

            _orchestrator.OnHotkeyPressed();

            Assert.Equal(SpeechError.ModelMissing, NextIdle().Error);
        }

        [Fact]
        public void GgufModel_IsUnsupported()
        {
            File.WriteAllBytes(_modelPath, "GGUF"u8.ToArray());

            _orchestrator.OnHotkeyPressed();

            Assert.Equal(SpeechError.ModelUnsupported, NextIdle().Error);
            Assert.Empty(_recorder.StartedDevices);
        }

        [Fact]
        public void MicrophoneError_IsReported()
        {
            _recorder.StartError = new SpeechException(SpeechError.MicrophoneUnavailable, "No microphone");

            Dictate();

            SpeechStateChangedEventArgs change = NextIdle();
            Assert.Equal(SpeechOutcome.Failed, change.Outcome);
            Assert.Equal(SpeechError.MicrophoneUnavailable, change.Error);
        }

        // The model loads while the user speaks; if it cannot, they learn it before they finish
        [Fact]
        public void ModelLoadFailure_StopsTheRecording()
        {
            _recognizer.PreloadError = new SpeechException(SpeechError.CpuUnsupported, "No AVX2");

            _orchestrator.OnHotkeyPressed();

            Assert.Equal(SpeechState.Recording, Next().State);
            SpeechStateChangedEventArgs change = Next();
            Assert.Equal(SpeechOutcome.Failed, change.Outcome);
            Assert.Equal(SpeechError.CpuUnsupported, change.Error);
            Assert.False(_recorder.IsRecording);

            _orchestrator.OnHotkeyReleased(); // the release of that press changes nothing
            Assert.False(_events.TryTake(out _, 200));
        }

        [Fact]
        public void RecognizerError_IsReported()
        {
            _recognizer.Error = new SpeechException(SpeechError.RuntimeMissing, "whisper.dll not found");

            Dictate();

            SpeechStateChangedEventArgs change = NextIdle();
            Assert.Equal(SpeechError.RuntimeMissing, change.Error);
            Assert.Equal("whisper.dll not found", change.Detail);
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public void UnexpectedError_IsReportedAsAFailure()
        {
            _recognizer.Error = new InvalidOperationException("boom");

            Dictate();

            SpeechStateChangedEventArgs change = NextIdle();
            Assert.Equal(SpeechError.Failed, change.Error);
            Assert.Equal("boom", change.Detail);
        }

        [Fact]
        public void CancelWhileRecording_DiscardsTheRecording()
        {
            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);

            _orchestrator.Cancel();

            Assert.Equal(SpeechOutcome.Cancelled, NextIdle().Outcome);
            Assert.False(_recorder.IsRecording);
            _orchestrator.OnHotkeyReleased();
            Assert.False(_events.TryTake(out _, 200));
            Assert.Empty(_recognizer.Transcriptions);
        }

        [Fact]
        public void CancelWhileTranscribing_PastesNothing()
        {
            _recognizer.Gate = new TaskCompletionSource();
            Dictate();
            Assert.Equal(SpeechState.Recording, Next().State);
            Assert.Equal(SpeechState.Transcribing, Next().State);

            _orchestrator.Cancel();

            Assert.Equal(SpeechOutcome.Cancelled, NextIdle().Outcome);
            _recognizer.Gate.SetResult();
            Assert.False(_events.TryTake(out _, 200));
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public void PressDuringTranscription_IsIgnoredWithItsRelease()
        {
            _recognizer.Gate = new TaskCompletionSource();
            Dictate();
            Assert.Equal(SpeechState.Recording, Next().State);
            Assert.Equal(SpeechState.Transcribing, Next().State);

            Dictate();
            Assert.False(_events.TryTake(out _, 200));
            _recognizer.Gate.SetResult();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.Single(_recognizer.Transcriptions);
            Assert.Single(_recorder.StartedDevices);

            _recognizer.Gate = null;
            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
        }

        [Fact]
        public void TimeLimit_EndsTheRecording()
        {
            Recreate(maxRecording: TimeSpan.FromMilliseconds(100));

            _orchestrator.OnHotkeyPressed();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            _orchestrator.OnHotkeyReleased();
            Assert.False(_events.TryTake(out _, 200));
        }

        [Fact]
        public void Disabled_IgnoresTheHotkey()
        {
            _orchestrator.Configure(SpeechSettings.Disabled);

            Dictate();

            Assert.False(_events.TryTake(out _, 200));
            Assert.Empty(_recorder.StartedDevices);
        }

        [Fact]
        public void DisablingWhileRecording_CancelsIt()
        {
            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);

            _orchestrator.Configure(SpeechSettings.Disabled);

            Assert.Equal(SpeechOutcome.Cancelled, NextIdle().Outcome);
            Assert.False(_recorder.IsRecording);
        }

        // The model is loaded as soon as dictation is configured, so the first dictation does not wait for it
        [Fact]
        public async Task Configure_LoadsTheModelAheadOfTime()
        {
            await WaitUntil(() => _recognizer.Preloads.Count > 0);

            Assert.Equal(new SpeechOptions(_modelPath, "uk", false), _recognizer.Preloads[0]);
            Assert.Empty(_recorder.StartedDevices);
        }

        [Fact]
        public async Task Configure_WithoutAModelOrDisabled_LoadsNothing()
        {
            await WaitUntil(() => _recognizer.Preloads.Count > 0); // the one made for the constructor's settings
            Recreate(modelPath: _modelPath + ".missing");
            lock (_recognizer.Preloads) _recognizer.Preloads.Clear();

            _orchestrator.Configure(SpeechSettings.Disabled);
            await Task.Delay(200);

            Assert.Empty(_recognizer.Preloads);
        }

        // A load ahead of time that fails is not reported: the hotkey press tries again and reports it then
        [Fact]
        public async Task FailedLoadAheadOfTime_IsReportedOnlyWhenDictating()
        {
            _recognizer.PreloadError = new SpeechException(SpeechError.CpuUnsupported, "No AVX2");
            Recreate();
            await WaitUntil(() => _recognizer.Preloads.Count > 0);
            Assert.False(_events.TryTake(out _, 200));

            _orchestrator.OnHotkeyPressed();

            Assert.Equal(SpeechState.Recording, Next().State);
            Assert.Equal(SpeechError.CpuUnsupported, NextIdle().Error);
        }

        // Shown before the microphone is open, so the press is acknowledged at once
        [Fact]
        public void Recording_IsReportedBeforeTheMicrophoneOpens()
        {
            _recorder.StartError = new SpeechException(SpeechError.MicrophoneUnavailable, "No microphone");

            _orchestrator.OnHotkeyPressed();

            Assert.Equal(SpeechState.Recording, Next().State);
            Assert.Equal(SpeechError.MicrophoneUnavailable, NextIdle().Error);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(watch.Elapsed < WaitLimit, "The condition was not met in time");
                await Task.Delay(10);
            }
        }

        [Fact]
        public void RecentAudioIsReportedOnlyWhileRecording()
        {
            var buffer = new float[512];
            Assert.Equal(0, _orchestrator.CopyRecentAudio(buffer));
            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);

            Assert.Equal(512, _orchestrator.CopyRecentAudio(buffer));
            Assert.Equal(0.25f, buffer[^1]);

            _orchestrator.OnHotkeyReleased();
            NextIdle();
            Assert.Equal(0, _orchestrator.CopyRecentAudio(buffer));
            Assert.Equal(0f, buffer[^1]);
        }

        // The next dictation starts at once on the microphone that is still open
        [Fact]
        public void Microphone_StaysOpenBetweenDictations()
        {
            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.True(_recorder.IsOpen);

            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);

            Assert.Equal(1, _recorder.OpenCount);
            Assert.Equal(0, _recorder.CloseCalls);
        }

        [Fact]
        public async Task Microphone_ClosesAfterItIsIdle()
        {
            Recreate(keepMicrophoneOpen: TimeSpan.FromMilliseconds(100));

            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);

            await WaitUntil(() => !_recorder.IsOpen);
            Assert.Equal(1, _recorder.CloseCalls);
        }

        [Fact]
        public async Task Press_CancelsThePendingClose()
        {
            Recreate(keepMicrophoneOpen: TimeSpan.FromMilliseconds(300));
            Dictate();
            NextIdle();

            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);
            await Task.Delay(500);

            Assert.True(_recorder.IsRecording);
            Assert.Equal(0, _recorder.CloseCalls);
            _orchestrator.OnHotkeyReleased();
            NextIdle();
            await WaitUntil(() => !_recorder.IsOpen);
        }

        [Fact]
        public async Task Disabling_ClosesTheMicrophoneAtOnce()
        {
            Dictate();
            NextIdle();

            _orchestrator.Configure(SpeechSettings.Disabled);

            await WaitUntil(() => !_recorder.IsOpen);
        }

        [Fact]
        public void Dispose_ClosesTheMicrophone()
        {
            Dictate();
            NextIdle();

            _orchestrator.Dispose();

            Assert.False(_recorder.IsOpen);
        }

        // The audio still on its way and the end of the last word are recorded after the release
        [Fact]
        public void Release_KeepsRecordingTheTrailingAudio()
        {
            Recreate(trailingAudio: TimeSpan.FromMilliseconds(150));
            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);

            long released = System.Diagnostics.Stopwatch.GetTimestamp();
            _orchestrator.OnHotkeyReleased();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(released, _recorder.StoppedAt) >= TimeSpan.FromMilliseconds(140));
        }

        [Fact]
        public void PressDuringTheTrailingAudio_IsIgnoredWithItsRelease()
        {
            Recreate(trailingAudio: TimeSpan.FromMilliseconds(300));
            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);
            _orchestrator.OnHotkeyReleased();

            Dictate();

            Assert.Equal(SpeechState.Transcribing, Next().State);
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.False(_events.TryTake(out _, 200));
            Assert.Single(_recorder.StartedDevices);
        }

        [Fact]
        public void DetectedSpeech_IsCutOutOfTheRecording()
        {
            var detector = new FakeSpeechDetector
            {
                Segments = new[]
                {
                    new SpeechSegment(TimeSpan.Zero, TimeSpan.FromSeconds(0.5)),
                    new SpeechSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
                },
            };
            Recreate(detector: detector);
            _recorder.Recording = FakeAudioRecorder.Speech(3);

            Dictate();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.Equal(24000, _recognizer.SampleCount);
        }

        [Fact]
        public void NoSpeechDetected_AfterADeliberateHold_IsReported()
        {
            Recreate(detector: new FakeSpeechDetector { Segments = Array.Empty<SpeechSegment>() }, noticeableHold: TimeSpan.Zero);

            Dictate();

            Assert.Equal(SpeechOutcome.NoSpeech, NextIdle().Outcome);
            Assert.Empty(_recognizer.Transcriptions);
        }

        [Fact]
        public void NoSpeechDetected_AfterATap_IsDiscardedQuietly()
        {
            Recreate(detector: new FakeSpeechDetector { Segments = Array.Empty<SpeechSegment>() });

            Dictate();

            Assert.Equal(SpeechOutcome.Discarded, NextIdle().Outcome);
            Assert.Empty(_recognizer.Transcriptions);
        }

        // A muted microphone or a wrong input: the user is told to check it, and no detector is needed for that
        [Fact]
        public void NoSignal_AfterADeliberateHold_IsReported()
        {
            var detector = new FakeSpeechDetector();
            Recreate(detector: detector, noticeableHold: TimeSpan.Zero);
            _recorder.Recording = Scale(FakeAudioRecorder.Speech(1), 0.002f); // peak -68 dBFS

            Dictate();

            Assert.Equal(SpeechOutcome.NoSignal, NextIdle().Outcome);
            Assert.Equal(0, detector.Calls);
            Assert.Empty(_recognizer.Transcriptions);
        }

        [Fact]
        public void DigitalSilence_AfterADeliberateHold_IsReportedAsNoSignal()
        {
            Recreate(noticeableHold: TimeSpan.Zero);
            _recorder.Recording = new float[16000];

            Dictate();

            Assert.Equal(SpeechOutcome.NoSignal, NextIdle().Outcome);
        }

        // Without a detector, quiet speech (an audio interface with little gain) is still transcribed
        [Fact]
        public void QuietSpeech_WithoutADetector_IsTranscribed()
        {
            _recorder.Recording = Scale(FakeAudioRecorder.Speech(1), 0.02f); // peak -48 dBFS

            Dictate();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
        }

        [Fact]
        public void DetectorNotInstalled_FallsBackToTheEnergyThreshold()
        {
            var detector = new FakeSpeechDetector { Segments = null };
            Recreate(detector: detector);
            _recorder.Recording = FakeAudioRecorder.Speech(2);

            Dictate();

            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Assert.Equal(32000, _recognizer.SampleCount); // whole
            Assert.Equal(1, detector.Calls);
        }

        [Fact]
        public void DetectorFailure_FallsBackToTheEnergyThresholdForGood()
        {
            var detector = new FakeSpeechDetector { Error = new InvalidOperationException("boom") };
            Recreate(detector: detector);

            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);

            Assert.Equal(1, detector.Calls);
        }

        // Voice input turned off frees the model's memory; turned on again, it loads it again
        [Fact]
        public async Task Disabling_FreesTheModel()
        {
            await WaitUntil(() => _recognizer.Preloads.Count > 0);

            _orchestrator.Configure(SpeechSettings.Disabled);
            await WaitUntil(() => _recognizer.Unloads > 0);

            lock (_recognizer.Preloads) _recognizer.Preloads.Clear();
            _orchestrator.Configure(Settings(_modelPath));
            await WaitUntil(() => _recognizer.Preloads.Count > 0);
        }

        // A deleted model is freed although voice input stays on
        [Fact]
        public async Task MissingModel_IsFreed()
        {
            File.Delete(_modelPath);

            _orchestrator.Configure(Settings(_modelPath));

            await WaitUntil(() => _recognizer.Unloads > 0);
        }

        // "Я думаю, що" then "Так буде краще." becomes one sentence
        [Fact]
        public void NextPhrase_ContinuesThePreviousDictation()
        {
            var target = new FakeDictationTarget();
            using var context = new DictationContext(target);
            Recreate(context: context);

            _recognizer.Segments = new[] { " Я думаю, що" };
            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);
            _recognizer.Segments = new[] { " Так буде краще." };
            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);

            Assert.Equal(" так буде краще.", _clipboard.LastSetText);
        }

        [Fact]
        public void AfterAKeyPress_ThePhraseStandsAlone()
        {
            var target = new FakeDictationTarget();
            using var context = new DictationContext(target);
            Recreate(context: context);

            _recognizer.Segments = new[] { " Я думаю, що" };
            Dictate();
            NextIdle();
            target.Interrupt();
            _recognizer.Segments = new[] { " Так." };
            Dictate();
            NextIdle();

            Assert.Equal("Так.", _clipboard.LastSetText);
        }

        private static float[] Scale(float[] samples, float factor)
        {
            for (int i = 0; i < samples.Length; i++) samples[i] *= factor;
            return samples;
        }
    }
}
