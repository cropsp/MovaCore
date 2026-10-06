using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class TranscriptTextTests
    {
        [Fact]
        public void Segments_AreJoinedWithSingleSpaces()
        {
            Assert.Equal("Привіт, світе. Як справи?", TranscriptText.Clean(new[] { " Привіт, світе.", "  Як справи?  " }));
        }

        [Theory]
        [InlineData("[BLANK_AUDIO]")]
        [InlineData("[Музика]")]
        [InlineData("(music)")]
        [InlineData("*laughs*")]
        [InlineData("♪")]
        [InlineData(" ♪ ♫ ")]
        public void SoundAnnotations_AreDropped(string annotation)
        {
            Assert.Equal("Текст", TranscriptText.Clean(new[] { annotation, " Текст", annotation }));
        }

        [Fact]
        public void OnlyAnnotations_GiveEmptyText()
        {
            Assert.Equal("", TranscriptText.Clean(new[] { "[BLANK_AUDIO]", " " }));
        }

        [Fact]
        public void AnnotationInsideSpeech_IsKept()
        {
            Assert.Equal("(laughs) So anyway", TranscriptText.Clean(new[] { "(laughs) So anyway" }));
        }

        [Fact]
        public void InnerWhitespace_IsCollapsed()
        {
            Assert.Equal("a b c", TranscriptText.Clean(new[] { "a \t b\n", "  c" }));
        }

        [Fact]
        public void NoSegments_GiveEmptyText()
        {
            Assert.Equal("", TranscriptText.Clean(Array.Empty<string>()));
        }

        [Theory]
        [InlineData("Хм, я думаю, що так.", "Я думаю, що так.")]
        [InlineData("Uhm, let me think.", "Let me think.")]
        [InlineData("Ну, ммм, добре.", "Ну, добре.")]
        [InlineData("Так, хм.", "Так.")]
        [InlineData("Це було. Хм. Цікаво.", "Це було. Цікаво.")]
        [InlineData("Хм?", "Хм?")]
        [InlineData("Хмара пливе.", "Хмара пливе.")]
        public void Hesitations_AreRemoved(string recognized, string expected)
        {
            Assert.Equal(expected, TranscriptText.Clean(new[] { recognized }));
        }

        [Theory]
        [InlineData("так так так так.", "так.")]
        [InlineData("Я я я думаю", "Я думаю")]
        [InlineData("дуже дуже добре", "дуже дуже добре")]
        [InlineData("ні, ні, ні.", "ні, ні, ні.")]
        public void WordsRepeatedThreeTimes_AreKeptOnce(string recognized, string expected)
        {
            Assert.Equal(expected, TranscriptText.Clean(new[] { recognized }));
        }

        [Fact]
        public void OnlyHesitations_GiveEmptyText()
        {
            Assert.Equal("", TranscriptText.Clean(new[] { " Хм.", " Ммм." }));
        }
    }
}
