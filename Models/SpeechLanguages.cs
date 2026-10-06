using System.Collections.Generic;

namespace MovaCore.Models
{
    /// <summary>The Whisper language codes offered in the settings, in display order.</summary>
    public static class SpeechLanguages
    {
        public const string Auto = "auto";

        public static IReadOnlyList<string> Codes { get; } = new[] { Auto, "uk", "en", "pl", "de", "fr", "es", "it", "pt" };
    }
}
