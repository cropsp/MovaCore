namespace MovaCore.Services
{
    /// <summary>How a dictation ended.</summary>
    public enum SpeechOutcome
    {
        Pasted,

        /// <summary>Recognized, but the application did not take the paste: the text is on the clipboard.</summary>
        NotPasted,

        /// <summary>Too short, silent, or nothing recognized: nothing to paste.</summary>
        Discarded,

        Cancelled,
        Failed,
    }
}
