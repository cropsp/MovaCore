using System.Text;
using SharpHook.Data;

namespace MovaCore.Models
{
    /// <summary>A trigger key together with the modifiers that must be held, e.g. Ctrl + Shift + F10.</summary>
    public readonly record struct Hotkey(KeyCode Key, HotkeyModifiers Modifiers)
    {
        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Ctrl + ");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift + ");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt + ");
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win + ");

            string key = Key.ToString();
            return sb.Append(key.StartsWith("Vc") ? key.Substring(2) : key).ToString();
        }
    }
}
