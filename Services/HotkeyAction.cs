namespace MovaCore.Services
{
    /// <summary>What the keyboard hook does with a key event, as decided by <see cref="HotkeyStateTracker"/>.</summary>
    public enum HotkeyAction
    {
        /// <summary>Not ours: the application gets the event.</summary>
        PassThrough,

        /// <summary>Ours, with nothing else to do (an auto-repeat of a held hotkey).</summary>
        Suppress,

        /// <summary>First press of the conversion trigger: suppress it.</summary>
        TriggerPressed,

        /// <summary>Release of the conversion trigger: suppress it and convert.</summary>
        TriggerReleased,

        /// <summary>First press of the speech hotkey: suppress it and start recording.</summary>
        SpeechPressed,

        /// <summary>Release of the speech hotkey: suppress it and stop recording.</summary>
        SpeechReleased,
    }
}
