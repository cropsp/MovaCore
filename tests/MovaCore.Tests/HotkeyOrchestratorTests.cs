using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class HotkeyOrchestratorTests
    {
        private readonly FakeClipboard _clipboard = new();
        private readonly FakeHotkeyService _hotkeys = new();
        private readonly FakeLayoutSwitcher _layouts = new();
        private readonly HotkeyOrchestrator _orchestrator;
        private readonly List<string> _messages = new();
        private int _nothingSelected;

        public HotkeyOrchestratorTests()
        {
            _orchestrator = new HotkeyOrchestrator(_hotkeys, new LayoutConverterService(), _clipboard, _layouts)
            {
                CopyTimeout = TimeSpan.FromMilliseconds(200),
                PasteTimeout = TimeSpan.FromMilliseconds(200),
                RestoreDelay = TimeSpan.Zero,
                UnobservedPasteDelay = TimeSpan.Zero,
            };
            _orchestrator.ConversionFailed += (_, message) => _messages.Add(message);
            _orchestrator.NothingSelected += (_, _) => _nothingSelected++;
        }

        [Fact]
        public async Task SelectedText_IsConvertedAndPasted()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт", _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal(0, _nothingSelected);
        }

        [Fact]
        public async Task ClipboardIsRestored_AfterThePasteIsRead()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт", _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
        }

        // A clipboard manager read the converted text as soon as it was set: the paste cannot be seen, but the
        // clipboard is still restored (after a pause)
        [Fact]
        public async Task TextReadBeforeThePaste_ClipboardIsStillRestored()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _clipboard.ReadOnSet = true;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт", _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
            Assert.Equal(KeyboardLanguage.Ukrainian, Assert.Single(_layouts.Switches));
        }

        // Restoring before the app has read the converted text would make it paste the old clipboard content instead
        [Fact]
        public async Task ClipboardKeepsConvertedText_WhenThePasteIsNotObserved()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _clipboard.PasteObserved = false;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal(0, _clipboard.RestoreCalls);
            Assert.Equal("привіт", _clipboard.Text);
        }

        [Fact]
        public async Task ClipboardIsRestored_WhenThereIsNothingToConvert()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            uint sequenceAfterCopy = 0;
            _hotkeys.OnCopy = () =>
            {
                _clipboard.SimulateAppCopy("12345");
                sequenceAfterCopy = _clipboard.Sequence;
            };

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _hotkeys.PasteCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
            Assert.Equal(sequenceAfterCopy, _clipboard.RestoredExpectedSequence);
        }

        [Fact]
        public async Task ClipboardIsRestored_WhenWritingTheConvertedTextFails()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _clipboard.FailSet = true;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _hotkeys.PasteCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
        }

        [Fact]
        public async Task RestoreDisabled_LeavesTheConvertedText()
        {
            _orchestrator.RestoreClipboard = false;
            _clipboard.SimulateAppCopy("user clipboard");
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _clipboard.CaptureCalls);
            Assert.Equal(0, _clipboard.RestoreCalls);
            Assert.Equal("привіт", _clipboard.Text);
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
            Assert.Equal(0, _clipboard.RestoreCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
            Assert.Equal(1, _hotkeys.CopyCalls);
            Assert.Equal(1, _nothingSelected); // the user is told to select the text first
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

        // A dictation paste holds the same gate: a conversion meanwhile is dropped, not interleaved with it
        [Fact]
        public async Task SharedGateTaken_ConversionIsIgnored()
        {
            var gate = new ClipboardGate();
            var orchestrator = new HotkeyOrchestrator(_hotkeys, new LayoutConverterService(), _clipboard, _layouts, gate);
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");
            Assert.True(gate.TryEnter());

            await orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _hotkeys.CopyCalls);
            gate.Exit();
            await orchestrator.ExecuteConversionAsync();
            Assert.Equal(1, _hotkeys.CopyCalls);
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

        // Re-selecting the pasted text lets a second press convert it back
        [Fact]
        public async Task PastedText_IsSelectedAgainAndTheLayoutIsSwitched()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт", _clipboard.LastSetText);
            Assert.Equal(new[] { 6 }, _hotkeys.SelectLeftCalls);
            Assert.Equal(new[] { KeyboardLanguage.Ukrainian }, _layouts.Switches);
            Assert.Equal(1, _clipboard.RestoreCalls);
        }

        [Fact]
        public async Task UkrainianLayoutText_SwitchesTheLayoutToEnglish()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("Руддщ");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("Hello", _clipboard.LastSetText);
            Assert.Equal(new[] { KeyboardLanguage.English }, _layouts.Switches);
            Assert.Equal(new[] { 5 }, _hotkeys.SelectLeftCalls);
        }

        [Fact]
        public async Task SelectAndSwitchDisabled_PastesWithoutSelectingOrSwitching()
        {
            _orchestrator.SelectConvertedText = false;
            _orchestrator.SwitchLayout = false;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт", _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Empty(_hotkeys.SelectLeftCalls);
            Assert.Empty(_layouts.Switches);
        }

        [Fact]
        public async Task SelectDisabled_StillSwitchesTheLayout()
        {
            _orchestrator.SelectConvertedText = false;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Empty(_hotkeys.SelectLeftCalls);
            Assert.Equal(new[] { KeyboardLanguage.Ukrainian }, _layouts.Switches);
        }

        [Fact]
        public async Task SwitchDisabled_StillSelectsTheConvertedText()
        {
            _orchestrator.SwitchLayout = false;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(new[] { 6 }, _hotkeys.SelectLeftCalls);
            Assert.Empty(_layouts.Switches);
        }

        // The app has not read the converted text, so it may not have been pasted yet: nothing else is done
        [Fact]
        public async Task PasteNotObserved_DoesNotSelectSwitchOrRestore()
        {
            _clipboard.SimulateAppCopy("user clipboard");
            _clipboard.PasteObserved = false;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Empty(_hotkeys.SelectLeftCalls);
            Assert.Empty(_layouts.Switches);
            Assert.Equal(0, _clipboard.RestoreCalls);
        }

        [Theory]
        [InlineData("ghbdsn\nghbdsn", "привіт\nпривіт")]
        [InlineData("ghbdsn\r\nghbdsn", "привіт\r\nпривіт")]
        [InlineData("ghbdsn\r", "привіт\r")]
        public async Task MultiLineSelection_IsConvertedButNotSelectedAgain(string selection, string expected)
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy(selection);

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(expected, _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Empty(_hotkeys.SelectLeftCalls);
            Assert.Equal(new[] { KeyboardLanguage.Ukrainian }, _layouts.Switches);
        }

        // An emoji is one caret step but two UTF-16 units
        [Fact]
        public async Task SelectedAgain_CountsTextElementsNotUtf16Units()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn\U0001F600");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal("привіт\U0001F600", _clipboard.LastSetText);
            Assert.Equal(8, _clipboard.LastSetText!.Length);
            Assert.Equal(new[] { 7 }, _hotkeys.SelectLeftCalls);
        }

        [Fact]
        public async Task TextOfThreeHundredElements_IsSelectedAgain()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy(new string('f', 300));

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(new string('а', 300), _clipboard.LastSetText);
            Assert.Equal(new[] { 300 }, _hotkeys.SelectLeftCalls);
        }

        // One Shift+Left per character would take noticeable time
        [Fact]
        public async Task TextOfMoreThanThreeHundredElements_IsPastedButNotSelectedAgain()
        {
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy(new string('f', 301));

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(new string('а', 301), _clipboard.LastSetText);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Empty(_hotkeys.SelectLeftCalls);
            Assert.Equal(new[] { KeyboardLanguage.Ukrainian }, _layouts.Switches);
        }

        // With nothing selected the word before the caret is selected, and the second copy is converted as usual
        [Fact]
        public async Task ConvertLastWord_NothingSelected_SelectsTheWordLeftAndConvertsIt()
        {
            _orchestrator.ConvertLastWord = true;
            _hotkeys.OnSelectWordLeft = () => _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(1, _hotkeys.SelectWordLeftCalls);
            Assert.Equal(2, _hotkeys.CopyCalls);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal("привіт", _clipboard.LastSetText);
            Assert.Equal(new[] { 6 }, _hotkeys.SelectLeftCalls);
            Assert.Equal(new[] { KeyboardLanguage.Ukrainian }, _layouts.Switches);
            Assert.Equal(0, _nothingSelected);
        }

        [Fact]
        public async Task ConvertLastWordDisabled_NothingSelected_DoesNothing()
        {
            _hotkeys.OnSelectWordLeft = () => _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _hotkeys.SelectWordLeftCalls);
            Assert.Equal(1, _hotkeys.CopyCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
            Assert.Equal(0, _clipboard.SetCalls);
            Assert.Equal(1, _nothingSelected);
        }

        [Fact]
        public async Task ConvertLastWord_SomethingSelected_DoesNotSelectTheWordLeft()
        {
            _orchestrator.ConvertLastWord = true;
            _hotkeys.OnCopy = () => _clipboard.SimulateAppCopy("ghbdsn");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(0, _hotkeys.SelectWordLeftCalls);
            Assert.Equal(1, _hotkeys.CopyCalls);
            Assert.Equal(1, _hotkeys.PasteCalls);
            Assert.Equal("привіт", _clipboard.LastSetText);
        }

        [Fact]
        public async Task ConvertLastWord_NothingBeforeTheCaret_DoesNothingAfterOneAttempt()
        {
            _orchestrator.ConvertLastWord = true;
            _clipboard.SimulateAppCopy("user clipboard");

            await _orchestrator.ExecuteConversionAsync();

            Assert.Equal(1, _hotkeys.SelectWordLeftCalls);
            Assert.Equal(2, _hotkeys.CopyCalls);
            Assert.Equal(0, _hotkeys.PasteCalls);
            Assert.Equal(0, _clipboard.SetCalls);
            Assert.Equal("user clipboard", _clipboard.Text);
            Assert.Equal(1, _nothingSelected);
        }

        [Theory]
        [InlineData("привіт", 6)]
        [InlineData("a", 1)]
        [InlineData("hello world", 11)]
        [InlineData("a b", 3)]
        public void TryCountCaretSteps_SingleLineText_CountsCharacters(string text, int expected)
        {
            Assert.True(HotkeyOrchestrator.TryCountCaretSteps(text, out int steps));
            Assert.Equal(expected, steps);
        }

        // A caret step is a text element: an emoji, a flag, a family and a letter with a combining accent are one each
        [Theory]
        [InlineData("\U0001F600", 1)]
        [InlineData("a\U0001F600b", 3)]
        [InlineData("\U0001F1FA\U0001F1E6", 1)]
        [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467", 1)]
        [InlineData("e\u0301", 1)]
        [InlineData("и\u0306", 1)]
        public void TryCountCaretSteps_CountsTextElementsNotUtf16Units(string text, int expected)
        {
            Assert.True(HotkeyOrchestrator.TryCountCaretSteps(text, out int steps));
            Assert.Equal(expected, steps);
        }

        [Theory]
        [InlineData("a\nb")]
        [InlineData("a\rb")]
        [InlineData("a\r\nb")]
        [InlineData("\n")]
        [InlineData("abc\n")]
        public void TryCountCaretSteps_MultiLineText_IsRefused(string text)
        {
            Assert.False(HotkeyOrchestrator.TryCountCaretSteps(text, out _));
        }

        [Fact]
        public void TryCountCaretSteps_EmptyText_IsRefused()
        {
            Assert.False(HotkeyOrchestrator.TryCountCaretSteps(string.Empty, out int steps));
            Assert.Equal(0, steps);
        }

        [Theory]
        [InlineData(299, true)]
        [InlineData(300, true)]
        [InlineData(301, false)]
        [InlineData(1000, false)]
        public void TryCountCaretSteps_LimitIsThreeHundredElements(int length, bool expected)
        {
            Assert.Equal(expected, HotkeyOrchestrator.TryCountCaretSteps(new string('x', length), out _));
        }

        // The limit counts elements, not UTF-16 units: 300 emoji are 600 units but still within the limit
        [Fact]
        public void TryCountCaretSteps_LimitCountsElementsNotUtf16Units()
        {
            string threeHundredEmoji = string.Concat(Enumerable.Repeat("\U0001F600", 300));
            string moreThanThreeHundredEmoji = threeHundredEmoji + "\U0001F600";

            Assert.True(HotkeyOrchestrator.TryCountCaretSteps(threeHundredEmoji, out int steps));
            Assert.Equal(300, steps);
            Assert.False(HotkeyOrchestrator.TryCountCaretSteps(moreThanThreeHundredEmoji, out _));
        }
    }
}
