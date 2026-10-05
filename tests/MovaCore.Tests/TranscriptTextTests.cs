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
    }
}
