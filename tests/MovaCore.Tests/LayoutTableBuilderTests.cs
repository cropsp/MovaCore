using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class LayoutTableBuilderTests
    {
        private static readonly (string EnglishKeys, string UkrainianKeys) BuiltIn =
            (LayoutConverterService.DefaultEnglishKeys, LayoutConverterService.DefaultUkrainianKeys);

        private static (char English, char Ukrainian)[] BuiltInPairs() =>
            LayoutConverterService.DefaultEnglishKeys
                .Zip(LayoutConverterService.DefaultUkrainianKeys, (en, ua) => (en, ua))
                .ToArray();

        private static Dictionary<char, char> EnglishToUkrainian((string EnglishKeys, string UkrainianKeys) tables)
        {
            Assert.Equal(tables.EnglishKeys.Length, tables.UkrainianKeys.Length);

            var pairs = new Dictionary<char, char>();
            for (int i = 0; i < tables.EnglishKeys.Length; i++)
                Assert.True(pairs.TryAdd(tables.EnglishKeys[i], tables.UkrainianKeys[i]), $"'{tables.EnglishKeys[i]}' is paired twice");
            return pairs;
        }

        private static void AssertNoDuplicates((string EnglishKeys, string UkrainianKeys) tables)
        {
            Assert.Equal(tables.EnglishKeys.Length, tables.UkrainianKeys.Length);
            Assert.Equal(tables.EnglishKeys.Length, tables.EnglishKeys.Distinct().Count());
            Assert.Equal(tables.UkrainianKeys.Length, tables.UkrainianKeys.Distinct().Count());
        }

        [Fact]
        public void Build_NoPairs_YieldsTheBuiltInTables()
        {
            var tables = LayoutTableBuilder.Build(Array.Empty<(char, char)>());

            Assert.Equal(LayoutConverterService.DefaultEnglishKeys, tables.EnglishKeys);
            Assert.Equal(LayoutConverterService.DefaultUkrainianKeys, tables.UkrainianKeys);
        }

        [Fact]
        public void Build_TheBuiltInPairs_YieldTheBuiltInTables()
        {
            var tables = LayoutTableBuilder.Build(BuiltInPairs());

            Assert.Equal(BuiltIn, tables);
        }

        [Fact]
        public void Build_TheBuiltInPairsInReverseOrder_KeepAllPairsOfTheBuiltInTables()
        {
            var tables = LayoutTableBuilder.Build(BuiltInPairs().Reverse());

            AssertNoDuplicates(tables);
            Assert.Equal(EnglishToUkrainian(BuiltIn), EnglishToUkrainian(tables));
        }

        [Fact]
        public void Build_IdenticalCharacters_AreSkipped()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('1', '1'), ('0', '0'), ('!', '!') });

            Assert.Equal(BuiltIn, tables);
            Assert.DoesNotContain('1', tables.EnglishKeys);
            Assert.DoesNotContain('1', tables.UkrainianKeys);
        }

        // A key typing the same character in both layouts claims it: neither a later key nor a built-in pair maps it
        [Fact]
        public void Build_IdenticalPair_ReservesTheCharacter()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('a', 'a'), ('a', 'ж') });

            AssertNoDuplicates(tables);
            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.False(pairs.ContainsKey('a'));
            Assert.Equal('ж', pairs[';']);
        }

        [Fact]
        public void Build_DuplicateEnglishCharacter_KeepsTheFirstPairing()
        {
            // The second pairing of 'q' (e.g. a character produced by two keys) is dropped as a whole
            var tables = LayoutTableBuilder.Build(new[] { ('q', 'й'), ('q', 'ъ') });

            AssertNoDuplicates(tables);
            Assert.Equal('й', EnglishToUkrainian(tables)['q']);
            Assert.DoesNotContain('ъ', tables.UkrainianKeys);
            Assert.Equal(BuiltIn, tables);
        }

        [Fact]
        public void Build_DuplicateUkrainianCharacter_KeepsTheFirstPairing()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('x', 'щ'), ('z', 'щ') });

            AssertNoDuplicates(tables);
            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.Equal('щ', pairs['x']);
            Assert.Single(tables.UkrainianKeys, 'щ');

            // 'z' lost its pairing with 'щ' and falls back to the built-in 'я'
            Assert.Equal('я', pairs['z']);
        }

        // The built-in pairs only fill the gaps: ('o', 'щ') and ('x', 'ч') would pair a used character a second time
        [Fact]
        public void Build_BuiltInPairs_NeverPairACharacterThatIsAlreadyUsed()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('x', 'щ') });

            AssertNoDuplicates(tables);
            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.Equal('щ', pairs['x']);
            Assert.False(pairs.ContainsKey('o'), "'o' would have to pair with the already used 'щ'");
            Assert.DoesNotContain('ч', tables.UkrainianKeys);
        }

        [Fact]
        public void Build_KeysMissingFromTheInput_KeepTheBuiltInPairs()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('q', 'й') });

            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.Equal('й', pairs['q']);
            Assert.Equal('ф', pairs['a']);
            Assert.Equal('ж', pairs[';']);
            Assert.Equal('₴', pairs['~']);
        }

        [Fact]
        public void Build_InputPairsComeBeforeTheBuiltInPairs()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('a', 'ж'), ('b', 'ф') });

            Assert.StartsWith("ab", tables.EnglishKeys);
            Assert.StartsWith("жф", tables.UkrainianKeys);
        }

        [Fact]
        public void Build_RandomPairs_NeverYieldDuplicatesOrIdenticalPairs()
        {
            // Characters of the built-in tables (so the input conflicts with the fill-in pairs) plus digits and ґ
            string english = LayoutConverterService.DefaultEnglishKeys + "0123ё";
            string ukrainian = LayoutConverterService.DefaultUkrainianKeys + "0123ёЁ\\/";
            var random = new Random(7);
            (char English, char Ukrainian)[] builtIn = BuiltInPairs();

            for (int iteration = 0; iteration < 300; iteration++)
            {
                var input = new List<(char English, char Ukrainian)>();
                int count = random.Next(0, 70);
                for (int i = 0; i < count; i++)
                    input.Add((english[random.Next(english.Length)], ukrainian[random.Next(ukrainian.Length)]));

                var tables = LayoutTableBuilder.Build(input);
                string context = $"iteration {iteration}";

                AssertNoDuplicates(tables);
                Assert.True(tables.EnglishKeys.Zip(tables.UkrainianKeys).All(p => p.First != p.Second), context);

                // The builder must always produce tables the converter accepts
                _ = new LayoutConverterService(tables.EnglishKeys, tables.UkrainianKeys);

                // Every pair comes from the input or from the built-in tables
                Dictionary<char, char> pairs = EnglishToUkrainian(tables);
                foreach (var (en, ua) in pairs)
                    Assert.True(input.Contains((en, ua)) || builtIn.Contains((en, ua)), $"{context}: unexpected pair '{en}' <-> '{ua}'");

                // Every input and built-in pair is either taken or blocked by a character that is already used,
                // either by another pair or by an input key that types the same character in both layouts
                var claimedByIdentity = input.Where(p => p.English == p.Ukrainian).Select(p => p.English).ToHashSet();
                foreach (var (en, ua) in input.Concat(builtIn).Where(p => p.English != p.Ukrainian))
                {
                    bool taken = pairs.TryGetValue(en, out char paired) && paired == ua;
                    bool blocked = tables.EnglishKeys.Contains(en) || tables.UkrainianKeys.Contains(ua)
                        || claimedByIdentity.Contains(en) || claimedByIdentity.Contains(ua);
                    Assert.True(taken || blocked, $"{context}: pair '{en}' <-> '{ua}' was dropped without a conflict");
                }

                // The first input pair is always accepted unless it is an identity pair
                if (input.Count > 0 && input[0].English != input[0].Ukrainian)
                    Assert.Equal(input[0].Ukrainian, pairs[input[0].English]);
            }
        }

        // The older "Ukrainian" layout: ё/Ё on the backtick key, and the backslash key types '\' and '/'
        private static List<(char English, char Ukrainian)> StandardLayoutPairs()
        {
            var pairs = BuiltInPairs().Where(p => char.IsLetter(p.English)).ToList();
            pairs.Add(('`', 'ё'));
            pairs.Add(('~', 'Ё'));
            pairs.Add(('\\', '\\'));
            pairs.Add(('|', '/'));
            return pairs;
        }

        [Fact]
        public void Build_StandardLayout_KeepsItsOwnBacktickAndBackslashKeys()
        {
            var tables = LayoutTableBuilder.Build(StandardLayoutPairs());

            AssertNoDuplicates(tables);
            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.Equal('ё', pairs['`']);
            Assert.Equal('Ё', pairs['~']);
            Assert.Equal('/', pairs['|']);

            // '\' types '\' in both layouts: no mapping, and the built-in ґ pairing is not filled in for it
            Assert.False(pairs.ContainsKey('\\'));
            Assert.DoesNotContain('ґ', tables.UkrainianKeys);
            Assert.DoesNotContain('Ґ', tables.UkrainianKeys);
        }

        [Fact]
        public void Build_IdentityPair_BlocksOnlyThePairsOfItsCharacter()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('\\', '\\') });

            AssertNoDuplicates(tables);
            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.False(pairs.ContainsKey('\\'));
            Assert.Equal('Ґ', pairs['|']);
        }

        // Ukrainian Ґ is used by another key, so the built-in ('|', 'Ґ') pair would pair Ґ twice and is dropped
        [Fact]
        public void Build_UkrainianCharacterAlreadyUsedByAnotherKey_DropsTheBuiltInPair()
        {
            var tables = LayoutTableBuilder.Build(new[] { ('&', 'Ґ') });

            AssertNoDuplicates(tables);
            Dictionary<char, char> pairs = EnglishToUkrainian(tables);
            Assert.Equal('Ґ', pairs['&']);
            Assert.False(pairs.ContainsKey('|'));
            Assert.Single(tables.UkrainianKeys, 'Ґ');
            Assert.DoesNotContain('?', tables.UkrainianKeys);
        }

        [Fact]
        public void BuiltInTables_ConvertGheAndStayReversible()
        {
            var converter = new LayoutConverterService();

            // ґ = '\', а = f, н = y, о = j, к = r
            Assert.Equal("ґанок", converter.Convert("\\fyjr"));
            Assert.Equal("Ґанок", converter.Convert("|fyjr"));
            Assert.Equal("\\fyjr", converter.Convert("ґанок"));
            Assert.Equal("|fyjr", converter.Convert("Ґанок"));

            foreach (string text in new[] { "\\fyjr", "|fyjr", "ґанок", "Ґанок", "ґрунт.", "ghbdsn\\", "Ґ" })
                Assert.Equal(text, converter.Convert(converter.Convert(text)));

            // ґ exists only in the Ukrainian layout and '\' only in the English one, so each decides the direction
            Assert.Equal(KeyboardLanguage.English, converter.TargetOf("ґ"));
            Assert.Equal(KeyboardLanguage.Ukrainian, converter.TargetOf("\\"));
        }

        [Fact]
        public void Build_StandardLayout_ConvertsYoAndStaysReversible()
        {
            var (english, ukrainian) = LayoutTableBuilder.Build(StandardLayoutPairs());
            var converter = new LayoutConverterService(english, ukrainian);

            Assert.Equal("привіт", converter.Convert("ghbdsn"));
            Assert.Equal("ё", converter.Convert("`"));
            Assert.Equal("a\\b", converter.Convert(converter.Convert("a\\b")));
        }
    }
}
