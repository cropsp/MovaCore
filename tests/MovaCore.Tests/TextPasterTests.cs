using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class TextPasterTests
    {
        private readonly FakeClipboard _clipboard = new();
        private readonly FakeHotkeyService _hotkeys = new();
        private readonly ClipboardGate _gate = new();
        private readonly TextPaster _paster;

        public TextPasterTests()
        {
            _paster = new TextPaster(_hotkeys, _clipboard, _gate)
            {
                GateTimeout = TimeSpan.FromMilliseconds(100),
                PasteTimeout = TimeSpan.FromMilliseconds(100),
                RestoreDelay = TimeSpan.Zero,
                UnobservedPasteDelay = TimeSpan.Zero,
            };
        }

        [Fact]
        public async Task Text_IsPastedAndTheClipboardRestored()
        {
            _clipboard.SimulateAppCopy("user clipboard");

            PasteResult result = await _paster.PasteAsync("Привіт", restoreClipboard: true);

            Assert.Equal(PasteResult.Pasted, result);
            Assert.Equal("Привіт", _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
        }

        [Fact]
        public async Task PasteNotObserved_LeavesTheTextOnTheClipboard()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _clipboard.PasteObserved = false;

            PasteResult result = await _paster.PasteAsync("Привіт", restoreClipboard: true);

            Assert.Equal(PasteResult.NotObserved, result);
            Assert.Equal("Привіт", _clipboard.Text);
            Assert.Equal(0, _clipboard.RestoreCalls);
        }

        // A clipboard manager read the text as soon as it was set, so the paste itself cannot be seen: it counts as
        // pasted and the clipboard is restored after a pause (before, the dictated text was left on the clipboard)
        [Fact]
        public async Task TextReadBeforeThePaste_IsRestoredAfterAPause()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _clipboard.ReadOnSet = true;

            PasteResult result = await _paster.PasteAsync("Привіт", restoreClipboard: true);

            Assert.Equal(PasteResult.Pasted, result);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
        }

        [Fact]
        public async Task RestoreDisabled_KeepsTheText()
        {
            _clipboard.SimulateAppCopy("user clipboard");

            await _paster.PasteAsync("Привіт", restoreClipboard: false);

            Assert.Equal(0, _clipboard.CaptureCalls);
            Assert.Equal("Привіт", _clipboard.Text);
        }

        [Fact]
        public async Task ClipboardWriteFailure_DoesNotPaste()
        {
            _clipboard.FailSet = true;

            PasteResult result = await _paster.PasteAsync("Привіт", restoreClipboard: true);

            Assert.Equal(PasteResult.ClipboardFailed, result);
            Assert.Equal(0, _hotkeys.PasteCalls);
        }

        [Fact]
        public async Task GateHeldByAConversion_IsBusy()
        {
            Assert.True(_gate.TryEnter());

            PasteResult result = await _paster.PasteAsync("Привіт", restoreClipboard: true);

            Assert.Equal(PasteResult.Busy, result);
            Assert.Equal(0, _clipboard.SetCalls);
        }

        [Fact]
        public async Task Paste_WaitsForAConversionToFinish()
        {
            var paster = new TextPaster(_hotkeys, _clipboard, _gate)
            {
                GateTimeout = TimeSpan.FromSeconds(5), // generous: the test machine may be busy
                PasteTimeout = TimeSpan.FromMilliseconds(100),
                RestoreDelay = TimeSpan.Zero,
            };
            Assert.True(_gate.TryEnter());

            Task<PasteResult> paste = paster.PasteAsync("Привіт", restoreClipboard: true);
            await Task.Delay(20);
            Assert.Equal(0, _clipboard.SetCalls);
            _gate.Exit();

            Assert.Equal(PasteResult.Pasted, await paste);
        }

        [Fact]
        public async Task Gate_IsReleasedAfterwards()
        {
            await _paster.PasteAsync("Привіт", restoreClipboard: true);

            Assert.True(_gate.TryEnter());
        }
    }
}
