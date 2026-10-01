using MovaCore.Models;
using SharpHook.Data;
using Xunit;

namespace MovaCore.Tests
{
    public class HotkeyTests
    {
        [Fact]
        public void ToString_KeyWithoutModifiers_IsTheKeyName()
        {
            Assert.Equal("F10", new Hotkey(KeyCode.VcF10, HotkeyModifiers.None).ToString());
        }

        [Fact]
        public void ToString_AllModifiers_AreListedInFixedOrder()
        {
            var hotkey = new Hotkey(
                KeyCode.VcA,
                HotkeyModifiers.Win | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Control);

            Assert.Equal("Ctrl + Shift + Alt + Win + A", hotkey.ToString());
        }

        [Theory]
        [InlineData(HotkeyModifiers.Control, "Ctrl + F10")]
        [InlineData(HotkeyModifiers.Shift, "Shift + F10")]
        [InlineData(HotkeyModifiers.Alt, "Alt + F10")]
        [InlineData(HotkeyModifiers.Win, "Win + F10")]
        [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, "Ctrl + Alt + F10")]
        public void ToString_SomeModifiers_AreListedBeforeTheKey(HotkeyModifiers modifiers, string expected)
        {
            Assert.Equal(expected, new Hotkey(KeyCode.VcF10, modifiers).ToString());
        }

        [Theory]
        [InlineData(KeyCode.VcPause, "Pause")]
        [InlineData(KeyCode.VcScrollLock, "ScrollLock")]
        public void ToString_StripsTheVcPrefix(KeyCode key, string expected)
        {
            Assert.Equal(expected, new Hotkey(key, HotkeyModifiers.None).ToString());
        }

        [Fact]
        public void Hotkeys_WithTheSameKeyAndModifiers_AreEqual()
        {
            var first = new Hotkey(KeyCode.VcF10, HotkeyModifiers.Control);
            var second = new Hotkey(KeyCode.VcF10, HotkeyModifiers.Control);

            Assert.Equal(first, second);
            Assert.NotEqual(first, new Hotkey(KeyCode.VcF10, HotkeyModifiers.Shift));
        }
    }
}
