using MovaCore.Models;
using MovaCore.Services;
using SharpHook.Data;
using Xunit;

namespace MovaCore.Tests
{
    public class HotkeyMatchingTests
    {
        [Fact]
        public void FromMask_LeftCtrl_IsControl()
        {
            Assert.Equal(HotkeyModifiers.Control, HotkeyMatching.FromMask(EventMask.LeftCtrl));
        }

        [Fact]
        public void FromMask_RightShiftAndLeftAlt_IsShiftAndAlt()
        {
            HotkeyModifiers modifiers = HotkeyMatching.FromMask(EventMask.RightShift | EventMask.LeftAlt);

            Assert.Equal(HotkeyModifiers.Shift | HotkeyModifiers.Alt, modifiers);
        }

        [Fact]
        public void FromMask_LeftMeta_IsWin()
        {
            Assert.Equal(HotkeyModifiers.Win, HotkeyMatching.FromMask(EventMask.LeftMeta));
        }

        [Fact]
        public void FromMask_None_IsNone()
        {
            Assert.Equal(HotkeyModifiers.None, HotkeyMatching.FromMask(EventMask.None));
        }

        // Left and right keys are not distinguished
        [Theory]
        [InlineData(EventMask.LeftCtrl, HotkeyModifiers.Control)]
        [InlineData(EventMask.RightCtrl, HotkeyModifiers.Control)]
        [InlineData(EventMask.LeftShift, HotkeyModifiers.Shift)]
        [InlineData(EventMask.RightShift, HotkeyModifiers.Shift)]
        [InlineData(EventMask.LeftAlt, HotkeyModifiers.Alt)]
        [InlineData(EventMask.RightAlt, HotkeyModifiers.Alt)]
        [InlineData(EventMask.LeftMeta, HotkeyModifiers.Win)]
        [InlineData(EventMask.RightMeta, HotkeyModifiers.Win)]
        public void FromMask_LeftAndRightKeys_AreNotDistinguished(EventMask mask, HotkeyModifiers expected)
        {
            Assert.Equal(expected, HotkeyMatching.FromMask(mask));
        }

        [Fact]
        public void FromMask_AllFourModifiers_AreCombined()
        {
            EventMask mask = EventMask.LeftCtrl | EventMask.RightShift | EventMask.LeftAlt | EventMask.RightMeta;

            Assert.Equal(
                HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Win,
                HotkeyMatching.FromMask(mask));
        }

        [Fact]
        public void FromMask_LockKeys_AreIgnored()
        {
            Assert.Equal(HotkeyModifiers.None, HotkeyMatching.FromMask(EventMask.NumLock | EventMask.CapsLock));
            Assert.Equal(
                HotkeyModifiers.Control,
                HotkeyMatching.FromMask(EventMask.LeftCtrl | EventMask.NumLock | EventMask.CapsLock | EventMask.ScrollLock));
        }

        [Fact]
        public void FromMask_MouseButtons_AreIgnored()
        {
            Assert.Equal(HotkeyModifiers.None, HotkeyMatching.FromMask(EventMask.Button1));
        }

        [Fact]
        public void Matches_PlainTrigger_MatchesKeyWithoutModifiers()
        {
            var trigger = new Hotkey(KeyCode.VcF10, HotkeyModifiers.None);

            Assert.True(HotkeyMatching.Matches(trigger, KeyCode.VcF10, EventMask.None));
        }

        [Fact]
        public void Matches_PlainTrigger_IgnoresLockKeys()
        {
            var trigger = new Hotkey(KeyCode.VcF10, HotkeyModifiers.None);

            Assert.True(HotkeyMatching.Matches(trigger, KeyCode.VcF10, EventMask.NumLock | EventMask.CapsLock));
        }

        // Shift+F10 opens the context menu and Ctrl+F10 belongs to the application: neither is our hotkey
        [Theory]
        [InlineData(EventMask.LeftShift)]
        [InlineData(EventMask.RightShift)]
        [InlineData(EventMask.LeftCtrl)]
        [InlineData(EventMask.RightCtrl)]
        [InlineData(EventMask.LeftAlt)]
        [InlineData(EventMask.LeftMeta)]
        public void Matches_PlainTrigger_RejectsExtraModifiers(EventMask mask)
        {
            var trigger = new Hotkey(KeyCode.VcF10, HotkeyModifiers.None);

            Assert.False(HotkeyMatching.Matches(trigger, KeyCode.VcF10, mask));
        }

        [Fact]
        public void Matches_CombinationTrigger_MatchesTheHeldModifiers()
        {
            var trigger = new Hotkey(KeyCode.VcF10, HotkeyModifiers.Control | HotkeyModifiers.Shift);

            Assert.True(HotkeyMatching.Matches(trigger, KeyCode.VcF10, EventMask.LeftCtrl | EventMask.RightShift));
            Assert.True(HotkeyMatching.Matches(trigger, KeyCode.VcF10, EventMask.RightCtrl | EventMask.LeftShift));
        }

        [Theory]
        [InlineData(EventMask.None)]
        [InlineData(EventMask.LeftCtrl)]
        [InlineData(EventMask.RightShift)]
        [InlineData(EventMask.LeftCtrl | EventMask.RightShift | EventMask.LeftAlt)]
        [InlineData(EventMask.LeftAlt | EventMask.RightShift)]
        public void Matches_CombinationTrigger_RejectsMissingOrExtraModifiers(EventMask mask)
        {
            var trigger = new Hotkey(KeyCode.VcF10, HotkeyModifiers.Control | HotkeyModifiers.Shift);

            Assert.False(HotkeyMatching.Matches(trigger, KeyCode.VcF10, mask));
        }

        [Fact]
        public void Matches_DifferentKey_NeverMatches()
        {
            var plain = new Hotkey(KeyCode.VcF10, HotkeyModifiers.None);
            var combination = new Hotkey(KeyCode.VcF10, HotkeyModifiers.Control | HotkeyModifiers.Shift);

            Assert.False(HotkeyMatching.Matches(plain, KeyCode.VcF9, EventMask.None));
            Assert.False(HotkeyMatching.Matches(plain, KeyCode.VcA, EventMask.None));
            Assert.False(HotkeyMatching.Matches(combination, KeyCode.VcF11, EventMask.LeftCtrl | EventMask.RightShift));
        }

        [Theory]
        [InlineData(KeyCode.VcLeftControl)]
        [InlineData(KeyCode.VcRightControl)]
        [InlineData(KeyCode.VcLeftShift)]
        [InlineData(KeyCode.VcRightShift)]
        [InlineData(KeyCode.VcLeftAlt)]
        [InlineData(KeyCode.VcRightAlt)]
        [InlineData(KeyCode.VcLeftMeta)]
        [InlineData(KeyCode.VcRightMeta)]
        public void IsModifierKey_ModifierKeys_AreRecognized(KeyCode key)
        {
            Assert.True(HotkeyMatching.IsModifierKey(key));
        }

        [Theory]
        [InlineData(KeyCode.VcF10)]
        [InlineData(KeyCode.VcA)]
        [InlineData(KeyCode.VcEscape)]
        [InlineData(KeyCode.VcCapsLock)]
        [InlineData(KeyCode.VcNumLock)]
        [InlineData(KeyCode.VcUndefined)]
        public void IsModifierKey_OtherKeys_AreNotRecognized(KeyCode key)
        {
            Assert.False(HotkeyMatching.IsModifierKey(key));
        }
    }
}
