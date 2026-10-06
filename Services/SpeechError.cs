namespace MovaCore.Services
{
    /// <summary>Why dictation did not work. Every value except <see cref="Failed"/> is a condition the user can fix.</summary>
    public enum SpeechError
    {
        ModelMissing,
        ModelUnsupported,
        MicrophoneUnavailable,
        MicrophoneBlocked,
        CpuUnsupported,
        RuntimeMissing,
        ClipboardFailed,
        Failed,
    }
}
