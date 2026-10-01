using MovaCore.Models;
using SharpHook.Data;

namespace MovaCore.Services
{
    /// <summary>Hook-independent rules for recognizing the trigger hotkey.</summary>
    public static class HotkeyMatching
    {
        /// <summary>The modifiers held according to a hook event's mask.</summary>
        public static HotkeyModifiers FromMask(EventMask mask)
        {
            var modifiers = HotkeyModifiers.None;
            if ((mask & EventMask.Ctrl) != 0) modifiers |= HotkeyModifiers.Control;
            if ((mask & EventMask.Shift) != 0) modifiers |= HotkeyModifiers.Shift;
            if ((mask & EventMask.Alt) != 0) modifiers |= HotkeyModifiers.Alt;
            if ((mask & EventMask.Meta) != 0) modifiers |= HotkeyModifiers.Win;
            return modifiers;
        }

        public static bool IsModifierKey(KeyCode key) => key is
            KeyCode.VcLeftControl or KeyCode.VcRightControl or
            KeyCode.VcLeftShift or KeyCode.VcRightShift or
            KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
            KeyCode.VcLeftMeta or KeyCode.VcRightMeta;

        /// <summary>
        /// Modifiers must match exactly, so a plain F10 trigger leaves Shift+F10 (context menu) and Ctrl+F10 alone.
        /// </summary>
        public static bool Matches(Hotkey trigger, KeyCode key, EventMask mask) =>
            key == trigger.Key && FromMask(mask) == trigger.Modifiers;
    }
}
