using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class TranscriptJoinerTests
    {
        [Theory]
        // Unknown context: as Whisper wrote it
        [InlineData(null, "Привіт.", "Привіт.")]
        [InlineData("", "Привіт.", "Привіт.")]
        // After a finished sentence: a space, the capital stays
        [InlineData("Я вдома.", "Привіт.", " Привіт.")]
        [InlineData("Що?", "Нічого.", " Нічого.")]
        [InlineData("Він сказав: «Так.»", "Добре.", " Добре.")]
        // Mid-sentence: a space and a small letter
        [InlineData("Я думаю, що", "Так буде краще.", " так буде краще.")]
        [InlineData("Я піду в магазин", "І куплю хліб.", " і куплю хліб.")]
        [InlineData("I went home", "And slept.", " and slept.")]
        // Already followed by a space or a new line
        [InlineData("Я думаю, що ", "Так.", "так.")]
        [InlineData("Список:\n", "Перше.", "Перше.")]
        // Punctuation that attaches to the previous word
        [InlineData("Я думаю", ", що так.", ", що так.")]
        public void Join_FitsThePhraseToTheTextBefore(string? before, string text, string expected)
        {
            Assert.Equal(expected, TranscriptJoiner.Join(before, text));
        }

        // Words that are written with a capital mid-sentence too stay as they are
        [Theory]
        [InlineData("I think so.")]
        [InlineData("I'm here.")]
        [InlineData("USB works.")]
        [InlineData("iPhone.")]
        [InlineData("123 apples.")]
        public void Join_KeepsWordsThatAreAlwaysCapitalized(string text)
        {
            Assert.Equal(" " + text, TranscriptJoiner.Join("and then", text));
        }
    }
}
