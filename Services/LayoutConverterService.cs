using System;
using System.Collections.Generic;
using System.Text;

namespace MovaCore.Services
{
    public class LayoutConverterService : ILayoutConverterService
    {
        // Characters produced by the same physical key: EnKeys[i] (English layout) <-> UaKeys[i] (Ukrainian layout).
        // Both strings must stay the same length and have no duplicates, so each map is the exact inverse of the other.
        private const string EnKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&|`~";
        private const string UaKeys = "йцукенгшщзхїфівапролджєячсмитьбю.ЙЦУКЕНГШЩЗХЇФІВАПРОЛДЖЄЯЧСМИТЬБЮ,\"№;:?/'₴";

        private static readonly Dictionary<char, char> EnToUa = new();
        private static readonly Dictionary<char, char> UaToEn = new();

        static LayoutConverterService()
        {
            if (EnKeys.Length != UaKeys.Length)
                throw new InvalidOperationException("EnKeys and UaKeys must pair up character by character.");

            for (int i = 0; i < EnKeys.Length; i++)
            {
                EnToUa.Add(EnKeys[i], UaKeys[i]);
                UaToEn.Add(UaKeys[i], EnKeys[i]);
            }
        }

        public string Convert(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // One map for the whole string: characters such as ',' '.' '?' ';' exist in both layouts,
            // so choosing the direction per character made the conversion irreversible
            var map = DetectSourceIsUkrainian(text) ? UaToEn : EnToUa;

            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                // Characters outside the map (digits, spaces, text in the other layout) stay as they are
                sb.Append(map.TryGetValue(c, out char mappedChar) ? mappedChar : c);
            }

            return sb.ToString();
        }

        // Only characters that exist in a single layout vote (letters, brackets, №, ₴, etc.).
        // On a tie the text is assumed to be typed in the English layout instead of the Ukrainian one, the most common case.
        private static bool DetectSourceIsUkrainian(string text)
        {
            int en = 0, ua = 0;
            foreach (var c in text)
            {
                bool inEn = EnToUa.ContainsKey(c);
                bool inUa = UaToEn.ContainsKey(c);
                if (inEn && !inUa) en++;
                else if (inUa && !inEn) ua++;
            }
            return ua > en;
        }
    }
}
