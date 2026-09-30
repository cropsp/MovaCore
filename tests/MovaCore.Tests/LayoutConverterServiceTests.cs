using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LayoutConverter.App.Services;
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
        public async Task ConvertAsync_ReturnsExpected(string input, string expected)
        {
            string actual = await Converter.ConvertAsync(input);

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
        public async Task ConvertAsync_RoundTrip_ReturnsOriginal(string input)
        {
            string once = await Converter.ConvertAsync(input);
            string twice = await Converter.ConvertAsync(once);

            Assert.Equal(input, twice);
        }

        [Fact]
        public async Task ConvertAsync_RoundTrip_HoldsForRandomEnglishLayoutStrings()
        {
            const string alphabet = "qwertyuiop[]asdfghjkl;'zxcvbnm,./QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&|`~ 0123-";
            const string latinLetters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            await AssertRoundTripHoldsAsync(alphabet, latinLetters);
        }

        [Fact]
        public async Task ConvertAsync_RoundTrip_HoldsForRandomUkrainianLayoutStrings()
        {
            const string alphabet = "йцукенгшщзхїфівапролджєячсмитьбю.ЙЦУКЕНГШЩЗХЇФІВАПРОЛДЖЄЯЧСМИТЬБЮ,\"№;:?/'₴ 0123-";
            string cyrillicLetters = new string(alphabet.Where(c => char.IsLetter(c) && c > 127).ToArray());

            await AssertRoundTripHoldsAsync(alphabet, cyrillicLetters);
        }

        // Every generated string contains at least one letter from `guaranteedLetters`, so it always has
        // a layout-specific character and the direction is unambiguous.
        private static async Task AssertRoundTripHoldsAsync(string alphabet, string guaranteedLetters)
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
                string converted = await Converter.ConvertAsync(original);
                string roundTripped = await Converter.ConvertAsync(converted);

                Assert.True(
                    roundTripped == original,
                    $"Round-trip failed for \"{original}\": converted to \"{converted}\", then back to \"{roundTripped}\" (iteration {i}).");
            }
        }
    }
}
