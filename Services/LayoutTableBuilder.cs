using System.Collections.Generic;
using System.Text;

namespace MovaCore.Services
{
    /// <summary>
    /// Turns "same key, same Shift state" character pairs read from the installed layouts into the paired key strings
    /// <see cref="LayoutConverterService"/> expects.
    /// </summary>
    public static class LayoutTableBuilder
    {
        public static (string EnglishKeys, string UkrainianKeys) Build(IEnumerable<(char English, char Ukrainian)> keyPairs)
        {
            var english = new StringBuilder();
            var ukrainian = new StringBuilder();
            var usedEnglish = new HashSet<char>();
            var usedUkrainian = new HashSet<char>();

            void TryAdd(char en, char ua)
            {
                // A character produced by two keys (e.g. '\' on both backslash keys) keeps its first pairing, so each
                // map stays the inverse of the other
                if (usedEnglish.Contains(en) || usedUkrainian.Contains(ua)) return;

                // A key typing the same character in both layouts needs no mapping, but it still claims the character:
                // otherwise a built-in pair (e.g. '\' -> ґ) would be filled in for a layout that types '\' there
                if (en == ua)
                {
                    usedEnglish.Add(en);
                    usedUkrainian.Add(ua);
                    return;
                }

                english.Append(en);
                ukrainian.Append(ua);
                usedEnglish.Add(en);
                usedUkrainian.Add(ua);
            }

            foreach (var (en, ua) in keyPairs)
                TryAdd(en, ua);

            // Keys the installed layouts did not yield (e.g. dead keys of "US-International") keep the default mapping
            for (int i = 0; i < LayoutConverterService.DefaultEnglishKeys.Length; i++)
                TryAdd(LayoutConverterService.DefaultEnglishKeys[i], LayoutConverterService.DefaultUkrainianKeys[i]);

            return (english.ToString(), ukrainian.ToString());
        }
    }
}
