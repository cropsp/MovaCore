using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class SpeechModelCatalogTests
    {
        private static readonly string Directory = Path.Combine(Path.GetTempPath(), "models");

        [Fact]
        public void DefaultModel_IsTurboQ8()
        {
            SpeechModelInfo? model = SpeechModelCatalog.Find(SpeechModelCatalog.DefaultId);

            Assert.NotNull(model);
            Assert.Equal("ggml-large-v3-turbo-q8_0.bin", model.FileName);
            Assert.Same(model, SpeechModelCatalog.Models[0]);
        }

        [Fact]
        public void Ids_AndFileNames_AreUnique()
        {
            Assert.Equal(SpeechModelCatalog.Models.Count, SpeechModelCatalog.Models.Select(m => m.Id).Distinct().Count());
            Assert.Equal(SpeechModelCatalog.Models.Count, SpeechModelCatalog.Models.Select(m => m.FileName).Distinct().Count());
            Assert.DoesNotContain(SpeechModelCatalog.Models, m => m.Id == SpeechModelCatalog.CustomId);
        }

        // Every catalog model is checked against a pinned hash, not only against what the server says
        [Fact]
        public void EveryModel_HasAPinnedHash()
        {
            foreach (SpeechModelInfo model in SpeechModelCatalog.Models)
            {
                if (model.Sha1 != null)
                    Assert.Matches("^[0-9a-f]{40}$", model.Sha1);
                else
                    Assert.Matches("^[0-9a-f]{64}$", model.Sha256);
            }
        }

        [Fact]
        public void DownloadUrl_PointsAtWhisperCppOnHuggingFace()
        {
            Uri url = SpeechModelCatalog.DownloadUrl(SpeechModelCatalog.Models[0]);

            Assert.Equal("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q8_0.bin", url.ToString());
        }

        [Fact]
        public void ResolvePath_CatalogModel_IsInTheModelsDirectory()
        {
            Assert.Equal(
                Path.Combine(Directory, "ggml-large-v3-turbo-q5_0.bin"),
                SpeechModelCatalog.ResolvePath("large-v3-turbo-q5_0", @"C:\ignored.bin", Directory));
        }

        [Fact]
        public void ResolvePath_Custom_IsTheChosenFile()
        {
            Assert.Equal(@"D:\models\mine.bin", SpeechModelCatalog.ResolvePath(SpeechModelCatalog.CustomId, @"D:\models\mine.bin", Directory));
            Assert.Null(SpeechModelCatalog.ResolvePath(SpeechModelCatalog.CustomId, " ", Directory));
        }

        // A value edited by hand into settings.json falls back to the default model
        [Fact]
        public void UnknownId_SelectsTheDefault()
        {
            Assert.Equal(SpeechModelCatalog.DefaultId, SpeechModelCatalog.Selected("tiny")?.Id);
            Assert.Null(SpeechModelCatalog.Selected(SpeechModelCatalog.CustomId));
        }
    }
}
