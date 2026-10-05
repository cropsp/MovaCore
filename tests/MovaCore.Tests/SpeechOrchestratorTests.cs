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
        private readonly BlockingCollection<SpeechStateChangedEventArgs> _events = new();
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
            TimeSpan? minRecording = null, TimeSpan? maxRecording = null, TimeSpan? idleUnload = null, string? modelPath = "")
        {
            var paster = new TextPaster(_hotkeys, _clipboard, new ClipboardGate())
            {
                PasteTimeout = TimeSpan.FromMilliseconds(100),
                RestoreDelay = TimeSpan.Zero,
            };
            var orchestrator = new SpeechOrchestrator(_recorder, _recognizer, paster)
            {
                MinRecording = minRecording ?? TimeSpan.Zero,
                MaxRecording = maxRecording ?? TimeSpan.FromMinutes(1),
                IdleUnload = idleUnload ?? TimeSpan.FromMinutes(1),
            };
            orchestrator.StateChanged += (_, e) => _events.Add(e);
            orchestrator.Configure(Settings(modelPath == "" ? _modelPath : modelPath));
            return orchestrator;
        }

        private void Recreate(TimeSpan? minRecording = null, TimeSpan? maxRecording = null, TimeSpan? idleUnload = null,
            string? modelPath = "")
        {
            _orchestrator.Dispose();
            _orchestrator = Create(minRecording, maxRecording, idleUnload, modelPath);
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
            Assert.Equal(expected, Assert.Single(_recognizer.Preloads));
            Assert.Equal(expected, Assert.Single(_recognizer.Transcriptions));
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

        [Fact]
        public async Task IdleModel_IsUnloaded()
        {
            Recreate(idleUnload: TimeSpan.FromMilliseconds(100));

            Dictate();
            Assert.Equal(SpeechOutcome.Pasted, NextIdle().Outcome);

            await Task.Delay(400);
            Assert.Equal(1, _recognizer.UnloadCalls);
        }

        [Fact]
        public void LevelIsReportedOnlyWhileRecording()
        {
            Assert.Equal(0, _orchestrator.CurrentLevel);
            _orchestrator.OnHotkeyPressed();
            Assert.Equal(SpeechState.Recording, Next().State);

            Assert.Equal(0.5f, _orchestrator.CurrentLevel);
        }
    }
}
