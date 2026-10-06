using MovaCore.Models;
using MovaCore.Services;
using SharpHook.Data;
using Xunit;

namespace MovaCore.Tests
{
    public class HotkeyStateTrackerTests
    {
        private static readonly Hotkey F10 = new(KeyCode.VcF10, HotkeyModifiers.None);
        private static readonly Hotkey ScrollLock = new(KeyCode.VcScrollLock, HotkeyModifiers.None);
        private static readonly Hotkey CtrlF10 = new(KeyCode.VcF10, HotkeyModifiers.Control);

        private readonly HotkeyStateTracker _tracker = new();
        private int _exclusionChecks;
        private bool _excluded;

        private HotkeyAction Press(KeyCode key, EventMask mask = EventMask.None, bool simulated = false,
            Hotkey? trigger = null, Hotkey? speech = null)
        {
            return _tracker.OnKeyPressed(key, mask, simulated, trigger ?? F10, speech, () =>
            {
                _exclusionChecks++;
                return _excluded;
            });
        }

        [Fact]
        public void TriggerPressAndRelease_AreOurs()
        {
            Assert.Equal(HotkeyAction.TriggerPressed, Press(KeyCode.VcF10));
            Assert.Equal(F10, _tracker.Held);
            Assert.Equal(HotkeyAction.TriggerReleased, _tracker.OnKeyReleased(KeyCode.VcF10));
            Assert.Null(_tracker.Held);
        }

        [Fact]
        public void OtherKeys_PassThrough()
        {
            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcA));
            Assert.Equal(HotkeyAction.PassThrough, _tracker.OnKeyReleased(KeyCode.VcA));
        }

        [Fact]
        public void TriggerWithExtraModifier_PassesThrough()
        {
            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcF10, EventMask.LeftShift));
            Assert.Equal(HotkeyAction.PassThrough, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        [Fact]
        public void SimulatedPress_PassesThrough()
        {
            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcF10, simulated: true));
            Assert.Equal(0, _exclusionChecks);
        }

        // Windows repeats key-down while the key is held
        [Fact]
        public void AutoRepeat_IsSuppressedWithoutCheckingExclusionAgain()
        {
            Press(KeyCode.VcF10);

            Assert.Equal(HotkeyAction.Suppress, Press(KeyCode.VcF10));
            Assert.Equal(HotkeyAction.Suppress, Press(KeyCode.VcF10));
            Assert.Equal(1, _exclusionChecks);
            Assert.Equal(HotkeyAction.TriggerReleased, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        // The user let go of Ctrl before F10: the repeats and the release still belong to the hotkey
        [Fact]
        public void ModifierReleasedFirst_RepeatsAndReleaseStayOurs()
        {
            Assert.Equal(HotkeyAction.TriggerPressed, Press(KeyCode.VcF10, EventMask.LeftCtrl, trigger: CtrlF10));

            Assert.Equal(HotkeyAction.Suppress, Press(KeyCode.VcF10, EventMask.None, trigger: CtrlF10));
            Assert.Equal(HotkeyAction.TriggerReleased, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        [Fact]
        public void ExcludedApplication_KeepsItsShortcut()
        {
            _excluded = true;

            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcF10));
            Assert.Equal(HotkeyAction.PassThrough, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        [Fact]
        public void NonMatchingPress_DoesNotCheckExclusion()
        {
            Press(KeyCode.VcA);

            Assert.Equal(0, _exclusionChecks);
        }

        [Fact]
        public void SpeechHotkey_PressAndRelease()
        {
            Assert.Equal(HotkeyAction.SpeechPressed, Press(KeyCode.VcScrollLock, speech: ScrollLock));
            Assert.Equal(HotkeyAction.Suppress, Press(KeyCode.VcScrollLock, speech: ScrollLock));
            Assert.Equal(HotkeyAction.SpeechReleased, _tracker.OnKeyReleased(KeyCode.VcScrollLock));
        }

        [Fact]
        public void SpeechHotkey_IsIgnoredWhenNotSet()
        {
            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcScrollLock));
        }

        [Fact]
        public void SpeechHotkeyEqualToTrigger_IsTheTrigger()
        {
            Assert.Equal(HotkeyAction.TriggerPressed, Press(KeyCode.VcF10, speech: F10));
            Assert.Equal(HotkeyAction.TriggerReleased, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        // F10 converts, Ctrl+F10 dictates: the release is matched to the hotkey that was pressed
        [Fact]
        public void TwoHotkeysOnOneKey_ReleaseBelongsToThePressedOne()
        {
            Assert.Equal(HotkeyAction.SpeechPressed, Press(KeyCode.VcF10, EventMask.LeftCtrl, speech: CtrlF10));
            Assert.Equal(HotkeyAction.SpeechReleased, _tracker.OnKeyReleased(KeyCode.VcF10));

            Assert.Equal(HotkeyAction.TriggerPressed, Press(KeyCode.VcF10, speech: CtrlF10));
            Assert.Equal(HotkeyAction.TriggerReleased, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        [Fact]
        public void WhileOneHotkeyIsHeld_TheOtherPassesThrough()
        {
            Press(KeyCode.VcScrollLock, speech: ScrollLock);

            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcF10, speech: ScrollLock));
            Assert.Equal(HotkeyAction.PassThrough, _tracker.OnKeyReleased(KeyCode.VcF10));
            Assert.Equal(HotkeyAction.SpeechReleased, _tracker.OnKeyReleased(KeyCode.VcScrollLock));
        }

        [Fact]
        public void OtherKeysWhileHeld_PassThrough()
        {
            Press(KeyCode.VcF10);

            Assert.Equal(HotkeyAction.PassThrough, Press(KeyCode.VcA));
            Assert.Equal(HotkeyAction.PassThrough, _tracker.OnKeyReleased(KeyCode.VcA));
            Assert.Equal(HotkeyAction.TriggerReleased, _tracker.OnKeyReleased(KeyCode.VcF10));
        }

        [Fact]
        public void Reset_ForgetsTheHeldHotkey()
        {
            Press(KeyCode.VcF10);
            _tracker.Reset();

            Assert.Null(_tracker.Held);
            Assert.Equal(HotkeyAction.PassThrough, _tracker.OnKeyReleased(KeyCode.VcF10));
            Assert.Equal(HotkeyAction.TriggerPressed, Press(KeyCode.VcF10));
        }
    }
}
