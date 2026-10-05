using System;
using System.Globalization;

namespace MovaCore.Services
{
    /// <summary>
    /// Fits a dictated phrase to the text before the caret: Whisper starts every phrase with a capital letter and no
    /// space, as if it began a new text, but a phrase may continue the previous one.
    /// </summary>
    public static class TranscriptJoiner
    {
        private const string ClosingPunctuation = ",.!?:;)]}»…";

        /// <param name="before">The text just before the caret, or null if it is not known (the phrase stays as it is).</param>
        public static string Join(string? before, string text)
        {
            if (string.IsNullOrEmpty(before) || text.Length == 0) return text;

            if (!EndsSentence(before)) text = Decapitalize(text);
            if (!char.IsWhiteSpace(before[^1]) && ClosingPunctuation.IndexOf(text[0]) < 0) text = " " + text;
            return text;
        }

        // A sentence ends with . ! ? or …, possibly followed by closing quotes or brackets; an empty line too
        private static bool EndsSentence(string text)
        {
            ReadOnlySpan<char> end = text.AsSpan().TrimEnd(" \t");
            if (end.IsEmpty || end[^1] is '\n' or '\r') return true;
            end = end.TrimEnd("\"'»”’)]");
            return !end.IsEmpty && end[^1] is '.' or '!' or '?' or '…';
        }

        // "Так" → "так" and "І" → "і", but not "I", "I'm", "USB" or "iPhone"
        private static string Decapitalize(string text)
        {
            int end = 0;
            while (end < text.Length && char.IsLetter(text[end])) end++;
            if (end == 0 || !char.IsUpper(text[0])) return text;

            bool lowerCaseWord = end == 1 ? text[0] != 'I' : char.IsLower(text[1]);
            return lowerCaseWord ? char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..] : text;
        }
    }
}
