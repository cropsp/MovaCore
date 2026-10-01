using System;
using System.Collections.Generic;
using System.Text;
using MovaCore.Models;

namespace MovaCore.Services
{
    public class LayoutConverterService : ILayoutConverterService
    {
        // Characters produced by the same physical key in the standard Windows "US" and "Ukrainian" layouts:
        // DefaultEnglishKeys[i] <-> DefaultUkrainianKeys[i]. Used when the installed layouts cannot be read.
        public const string DefaultEnglishKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&|`~";
        public const string DefaultUkrainianKeys = "йцукенгшщзхїфівапролджєячсмитьбю.ЙЦУКЕНГШЩЗХЇФІВАПРОЛДЖЄЯЧСМИТЬБЮ,\"№;:?/'₴";

        private readonly Dictionary<char, char> _enToUa = new();
        private readonly Dictionary<char, char> _uaToEn = new();

        public LayoutConverterService()
            : this(DefaultEnglishKeys, DefaultUkrainianKeys)
        {
        }

        /// <summary>
        /// Both strings pair up character by character and must not contain duplicates, so each map is the exact
        /// inverse of the other.
        /// </summary>
        public LayoutConverterService(string englishKeys, string ukrainianKeys)
        {
            if (englishKeys.Length != ukrainianKeys.Length)
                throw new ArgumentException("The English and Ukrainian keys must pair up character by character.");

            for (int i = 0; i < englishKeys.Length; i++)
            {
                _enToUa.Add(englishKeys[i], ukrainianKeys[i]);
                _uaToEn.Add(ukrainianKeys[i], englishKeys[i]);
            }
        }

        public string Convert(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // One map for the whole string: characters such as ',' '.' '?' ';' exist in both layouts,
            // so choosing the direction per character made the conversion irreversible
            var map = TargetOf(text) == KeyboardLanguage.English ? _uaToEn : _enToUa;

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
        public KeyboardLanguage TargetOf(string text)
        {
            int en = 0, ua = 0;
            foreach (var c in text)
            {
                bool inEn = _enToUa.ContainsKey(c);
                bool inUa = _uaToEn.ContainsKey(c);
                if (inEn && !inUa) en++;
                else if (inUa && !inEn) ua++;
            }
            return ua > en ? KeyboardLanguage.English : KeyboardLanguage.Ukrainian;
        }
    }
}
