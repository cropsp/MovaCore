namespace MovaCore.Models
{
    /// <summary>Where the keyboard focus is: the foreground window and the control in it that has the focus.</summary>
    public readonly record struct DictationFocus(nint Window, nint Control);
}
