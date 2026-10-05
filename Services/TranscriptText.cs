using System.Collections.Generic;
using System.Text;

namespace MovaCore.Services
{
    /// <summary>Turns recognized segments into the text that gets pasted.</summary>
    public static class TranscriptText
    {
        /// <summary>
        /// Joins the segments with single spaces, dropping the ones that describe sounds instead of speech:
        /// "[BLANK_AUDIO]", "(music)", "[Музика]", "*laughs*", "♪".
        /// </summary>
        public static string Clean(IEnumerable<string> segments)
        {
            var text = new StringBuilder();
            foreach (string segment in segments)
            {
                string trimmed = segment.Trim();
                if (trimmed.Length == 0 || IsAnnotation(trimmed)) continue;

                if (text.Length > 0) text.Append(' ');
                text.Append(trimmed);
            }
            return CollapseWhitespace(text.ToString());
        }

        private static bool IsAnnotation(string segment)
        {
            char first = segment[0], last = segment[^1];
            if (segment.Length >= 2 && ((first == '[' && last == ']') || (first == '(' && last == ')') || (first == '*' && last == '*')))
                return true;

            foreach (char c in segment)
            {
                if (c != '♪' && c != '♫' && !char.IsWhiteSpace(c)) return false;
            }
            return true;
        }

        private static string CollapseWhitespace(string text)
        {
            var result = new StringBuilder(text.Length);
            bool pendingSpace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }
                if (pendingSpace) result.Append(' ');
                pendingSpace = false;
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
