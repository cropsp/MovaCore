using System;

namespace MovaCore.Models
{
    /// <summary>Modifier keys of a hotkey; left and right keys are not distinguished.</summary>
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
        Win = 8,
    }
}
