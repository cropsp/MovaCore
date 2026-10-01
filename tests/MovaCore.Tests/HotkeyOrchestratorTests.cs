using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class HotkeyOrchestratorTests
    {
        private readonly FakeClipboard _clipboard = new();
        private readonly FakeHotkeyService _hotkeys = new();
        private readonly HotkeyOrchestrator _orchestrator;
        private readonly List<string> _messages = new();

        public HotkeyOrchestratorTests()
        {
            _orchestrator = new HotkeyOrchestrator(_hotkeys, new LayoutConverterService(), _clipboard)
            {
                CopyTimeout = TimeSpan.FromMilliseconds(200)
            };
            _orchestrator.ConversionFailed += (_, message) => _messages.Add(message);
        }

        [Fact]
        public async Task SelectedText_IsConvertedAndPasted()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт", _clipboard.Text);
            Assert.Equal(1, _hotkeys.PasteCalls);
        }

        // Regression: with nothing selected the copy never reaches the clipboard,
        // and the stale clipboard content must not be converted and pasted.
        [Fact]
        public async Task NothingSelected_LeavesClipboardUntouched()
        {
            _clipboard.Text = "old clipboard text";
            _clipboard.Sequence = 5;

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("old clipboard text", _clipboard.Text);
            Assert.Equal(0, _clipboard.GetCalls);
            Assert.Equal(0, _clipboard.SetCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
            Assert.Equal(1, _hotkeys.CopyCalls);
        }

        [Fact]
        public async Task NonTextCopied_DoesNothing()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy(null);

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _clipboard.SetCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task WhitespaceSelection_DoesNothing()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("   ");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _clipboard.SetCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task TextWithoutLayoutCharacters_IsNotPasted()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("12345");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _clipboard.SetCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task ClipboardWriteFailure_ReportsErrorAndDoesNotPaste()
        {
            _clipboard.FailSet = true;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            string message = Assert.Single(_messages);
            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task SecondTriggerWhileBusy_IsIgnored()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            // The first call suspends on a delay while it polls the clipboard, so it is still busy
            // when the second call starts.
            var first = _orchestrator.ExecuteConversionAsync();
            var second = _orchestrator.ExecuteConversionAsync();

            Assert.True(second.IsCompleted);

            await Task.WhenAll(first, second);

            Assert.Equal(1, _hotkeys.CopyCalls);
            Assert.Equal(1, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task CanRunAgainAfterCompletion()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();
            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(2, _hotkeys.CopyCalls);
            Assert.Equal(2, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task ExceptionIsReportedAndBusyFlagIsReset()
        {
            _hotkeys.OnCopy = () => throw new InvalidOperationException("boom");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Contains("boom", Assert.Single(_messages));

            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(2, _hotkeys.CopyCalls);
            Assert.Equal(1, _hotkeys.PasteCalls);
        }
    }
}
