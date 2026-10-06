using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MovaCore.Services
{
    /// <summary>Turns recognized segments into the text that gets pasted.</summary>
    public static class TranscriptText
    {
        // Hesitation sounds that are never words, in any language (as Handy removes them)
        private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
        {
            "uh", "uhm", "umm", "uhh", "uhhh", "ehh", "ehm", "ahm", "hmm", "hm", "mmm", "хм", "хмм", "ммм",
        };

        /// <summary>
        /// Joins the segments with single spaces, dropping the ones that describe sounds instead of speech:
        /// "[BLANK_AUDIO]", "(music)", "[Музика]", "*laughs*", "♪". Hesitations ("хм", "uhm") go too, and a word
        /// repeated three or more times in a row (how Whisper loops) is kept once.
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
            return CollapseRepeats(RemoveFillers(CollapseWhitespace(text.ToString())));
        }

        private static string RemoveFillers(string text)
        {
            var kept = new List<string>();
            bool capitalizeNext = false;
            foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                (string word, string punctuation) = Split(token);
                // "Хм," and "hmm." go with their punctuation; "Хм?" stays, it may be the whole point
                if (Fillers.Contains(word) && punctuation is "" or "," or ".")
                {
                    bool sentenceStart = kept.Count == 0 || EndsSentence(kept[^1]);
                    if (sentenceStart && char.IsUpper(word[0])) capitalizeNext = true;
                    // "Так, хм." keeps its full stop
                    if (punctuation == "." && kept.Count > 0 && kept[^1].EndsWith(',')) kept[^1] = kept[^1][..^1] + ".";
                    continue;
                }

                if (capitalizeNext && token.Length > 0 && char.IsLower(token[0]))
                    kept.Add(char.ToUpper(token[0], CultureInfo.CurrentCulture) + token[1..]);
                else
                    kept.Add(token);
                capitalizeNext = false;
            }
            return string.Join(' ', kept);
        }

        // "так так так так." → "так.": only words separated by nothing but spaces, the last may carry punctuation
        private static string CollapseRepeats(string text)
        {
            string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>(tokens.Length);
            int i = 0;
            while (i < tokens.Length)
            {
                (string word, string punctuation) = Split(tokens[i]);
                int end = i; // the last token of the run
                while (punctuation.Length == 0 && word.Length > 0 && end + 1 < tokens.Length)
                {
                    (string next, string nextPunctuation) = Split(tokens[end + 1]);
                    if (!string.Equals(next, word, StringComparison.CurrentCultureIgnoreCase)) break;
                    end++;
                    punctuation = nextPunctuation;
                }

                if (end - i + 1 >= 3)
                {
                    kept.Add(word + punctuation);
                    i = end + 1;
                }
                else
                {
                    kept.Add(tokens[i]);
                    i++;
                }
            }
            return string.Join(' ', kept);
        }

        // A token's word and the punctuation after it
        private static (string Word, string Punctuation) Split(string token)
        {
            int end = token.Length;
            while (end > 0 && char.IsPunctuation(token[end - 1])) end--;
            return (token[..end], token[end..]);
        }

        private static bool EndsSentence(string token) => token.Length > 0 && token[^1] is '.' or '!' or '?' or '…';

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
