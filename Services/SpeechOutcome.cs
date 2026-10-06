namespace MovaCore.Services
{
    /// <summary>How a dictation ended.</summary>
    public enum SpeechOutcome
    {
        Pasted,

        /// <summary>Recognized, but the application did not take the paste: the text is on the clipboard.</summary>
        NotPasted,

        /// <summary>Too short, or nothing recognized: nothing to paste.</summary>
        Discarded,

        /// <summary>Held long enough to mean it, but the recording holds no speech.</summary>
        NoSpeech,

        /// <summary>Held long enough to mean it, but the microphone delivered (almost) nothing: muted, or the wrong input.</summary>
        NoSignal,

        Cancelled,
        Failed,
    }
}
