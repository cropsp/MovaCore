using System.Text;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public sealed class SpeechModelFileTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"movacore-model-{Guid.NewGuid():N}.bin");

        public void Dispose() => File.Delete(_path);

        [Fact]
        public void GgmlMagic_IsGgml()
        {
            // whisper.cpp writes the magic 0x67676d6c as a little-endian uint32
            File.WriteAllBytes(_path, new byte[] { 0x6C, 0x6D, 0x67, 0x67, 1, 2, 3 });

            Assert.Equal(SpeechModelFormat.Ggml, SpeechModelFile.Check(_path));
        }

        [Fact]
        public void GgufMagic_IsGguf()
        {
            File.WriteAllBytes(_path, Encoding.ASCII.GetBytes("GGUF\u0003\0\0\0"));

            Assert.Equal(SpeechModelFormat.Gguf, SpeechModelFile.Check(_path));
        }

        [Theory]
        [InlineData("ggml")] // the magic spelled out is not what whisper.cpp writes
        [InlineData("PK\u0003\u0004")]
        [InlineData("lm")]
        [InlineData("")]
        public void OtherContent_IsUnknown(string content)
        {
            File.WriteAllBytes(_path, Encoding.ASCII.GetBytes(content));

            Assert.Equal(SpeechModelFormat.Unknown, SpeechModelFile.Check(_path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NoPath_IsMissing(string? path)
        {
            Assert.Equal(SpeechModelFormat.Missing, SpeechModelFile.Check(path));
        }

        [Fact]
        public void NoFile_IsMissing()
        {
            Assert.Equal(SpeechModelFormat.Missing, SpeechModelFile.Check(_path));
        }
    }
}
