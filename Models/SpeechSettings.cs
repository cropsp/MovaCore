using System;

namespace MovaCore.Models
{
    /// <summary>
    /// The dictation settings as the orchestrator uses them. <paramref name="ModelPath"/> is resolved from the chosen
    /// model (null: no model chosen); <paramref name="MicrophoneId"/> null means the Windows default microphone, and
    /// <paramref name="GpuName"/> null a graphics card chosen automatically. <paramref name="UnloadAfter"/> is how long the
    /// model stays loaded after a dictation (null: as long as dictation is on, zero: it is freed after every dictation).
    /// </summary>
    public sealed record SpeechSettings(
        bool Enabled,
        string? ModelPath,
        string Language,
        bool UseGpu,
        string? MicrophoneId,
        bool RestoreClipboard,
        bool FastRecognition = false,
        string? GpuName = null,
        TimeSpan? UnloadAfter = null)
    {
        public static SpeechSettings Disabled { get; } = new(false, null, SpeechLanguages.Auto, false, null, true);
    }
}
