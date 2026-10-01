using System;
using System.Linq;
using System.Text;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class LayoutConverterServiceTests
    {
        private static readonly LayoutConverterService Converter = new();

        [Theory]
        [InlineData("ghbdsn", "привіт")]
        [InlineData("Ghbdsn? cdsn", "Привіт, світ")]
        [InlineData("vj'", "моє")]
        [InlineData("'", "є")]
        [InlineData("v`znf", "м'ята")]
        [InlineData("\"dhjgf", "Європа")]
        [InlineData("~100", "₴100")]
        [InlineData("#1", "№1")]
        [InlineData("Руддщ", "Hello")]
        [InlineData("Куфддн,", "Really?")]
        [InlineData("₴5", "~5")]
        [InlineData("№1", "#1")]
        [InlineData("Nfr?", "Так,")]
        [InlineData("Так,", "Nfr?")]
        [InlineData("12345", "12345")]
        [InlineData("   ", "   ")]
        [InlineData("", "")]
        public void Convert_ReturnsExpected(string input, string expected)
        {
            string actual = Converter.Convert(input);

            Assert.Equal(expected, actual);
        }

        // Regression: ',' '.' '?' ';' exist in both layouts, so picking the direction per character
        // used to make the conversion non-invertible.
        [Theory]
        [InlineData("Nfr?")]
        [InlineData("ghbdsn? cdsn")]
        [InlineData("Так?")]
        [InlineData("моє")]
        [InlineData("Привіт, світ.")]
        public void Convert_RoundTrip_ReturnsOriginal(string input)
        {
            string once = Converter.Convert(input);
            string twice = Converter.Convert(once);

            Assert.Equal(input, twice);
        }

        [Fact]
        public void Convert_RoundTrip_HoldsForRandomEnglishLayoutStrings()
        {
            const string alphabet = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&|`~ 0123-";
            const string latinLetters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            AssertRoundTripHolds(alphabet, latinLetters);
        }

        [Fact]
        public void Convert_RoundTrip_HoldsForRandomUkrainianLayoutStrings()
        {
            const string alphabet = "йцукенгшщзхїфівапролджєячсмитьбю.ЙЦУКЕНГШЩЗХЇФІВАПРОЛДЖЄЯЧСМИТЬБЮ,\"№;:?/'₴ 0123-";
            string cyrillicLetters = new string(alphabet.Where(c => char.IsLetter(c) && c > 127).ToArray());

            AssertRoundTripHolds(alphabet, cyrillicLetters);
        }

        [Theory]
        [InlineData("ghbdsn", KeyboardLanguage.Ukrainian)]
        [InlineData("Ghbdsn? cdsn", KeyboardLanguage.Ukrainian)]
        [InlineData("руддщ", KeyboardLanguage.English)]
        [InlineData("Привіт, світ", KeyboardLanguage.English)]
        [InlineData("№1", KeyboardLanguage.English)]
        [InlineData("#1", KeyboardLanguage.Ukrainian)]
        public void TargetOf_IsTheLayoutTheTextIsConvertedInto(string text, KeyboardLanguage expected)
        {
            Assert.Equal(expected, Converter.TargetOf(text));
        }

        // On a tie the text is assumed to be typed in the English layout, the most common case
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("12345")]
        [InlineData(",.?;")]
        [InlineData("aф")]
        [InlineData("abфі")]
        public void TargetOf_NoVotesOrATie_IsUkrainian(string text)
        {
            Assert.Equal(KeyboardLanguage.Ukrainian, Converter.TargetOf(text));
        }

        // Only characters that exist in a single layout vote
        [Theory]
        [InlineData("a,.?;фі", KeyboardLanguage.English)]
        [InlineData("aб", KeyboardLanguage.Ukrainian)]
        [InlineData("abф", KeyboardLanguage.Ukrainian)]
        [InlineData("aфі", KeyboardLanguage.English)]
        public void TargetOf_CountsOnlyCharactersOfASingleLayout(string text, KeyboardLanguage expected)
        {
            Assert.Equal(expected, Converter.TargetOf(text));
        }

        [Fact]
        public void BuiltInKeys_PairUpWithoutDuplicates()
        {
            Assert.Equal(LayoutConverterService.DefaultEnglishKeys.Length, LayoutConverterService.DefaultUkrainianKeys.Length);
            Assert.Equal(LayoutConverterService.DefaultEnglishKeys.Length, LayoutConverterService.DefaultEnglishKeys.Distinct().Count());
            Assert.Equal(LayoutConverterService.DefaultUkrainianKeys.Length, LayoutConverterService.DefaultUkrainianKeys.Distinct().Count());
        }

        [Fact]
        public void Constructor_CustomKeys_AreUsedInBothDirections()
        {
            var converter = new LayoutConverterService("ab", "жф");

            Assert.Equal("жф", converter.Convert("ab"));
            Assert.Equal("ab", converter.Convert("жф"));
        }

        [Theory]
        [InlineData("abc", "жф")]
        [InlineData("ab", "ж")]
        [InlineData("", "ж")]
        [InlineData("a", "")]
        public void Constructor_KeysOfDifferentLengths_Throws(string englishKeys, string ukrainianKeys)
        {
            Assert.Throws<ArgumentException>(() => new LayoutConverterService(englishKeys, ukrainianKeys));
        }

        [Theory]
        [InlineData("aa", "жф")]
        [InlineData("ab", "жж")]
        public void Constructor_DuplicateCharacter_Throws(string englishKeys, string ukrainianKeys)
        {
            Assert.ThrowsAny<Exception>(() => new LayoutConverterService(englishKeys, ukrainianKeys));
        }

        [Fact]
        public void Constructor_NoKeys_ConvertsNothing()
        {
            var converter = new LayoutConverterService(string.Empty, string.Empty);

            Assert.Equal("ghbdsn", converter.Convert("ghbdsn"));
            Assert.Equal(KeyboardLanguage.Ukrainian, converter.TargetOf("ghbdsn"));
        }

        // Every generated string contains at least one letter from `guaranteedLetters`, so it always has
        // a layout-specific character and the direction is unambiguous.
        private static void AssertRoundTripHolds(string alphabet, string guaranteedLetters)
        {
            const int iterations = 2000;
            var random = new Random(1);

            for (int i = 0; i < iterations; i++)
            {
                var sb = new StringBuilder();
                int length = random.Next(0, 13);
                for (int j = 0; j < length; j++)
                    sb.Append(alphabet[random.Next(alphabet.Length)]);

                char letter = guaranteedLetters[random.Next(guaranteedLetters.Length)];
                sb.Insert(random.Next(sb.Length + 1), letter);

                string original = sb.ToString();
                string converted = Converter.Convert(original);
                string roundTripped = Converter.Convert(converted);

                Assert.True(
                    roundTripped == original,
                    $"Round-trip failed for \"{original}\": converted to \"{converted}\", then back to \"{roundTripped}\" (iteration {i}).");
            }
        }
    }
}
