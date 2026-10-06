using System;
using System.Collections.Generic;
using System.IO;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>The Whisper models MovaCore can download: variants of Large v3 Turbo from whisper.cpp's repository.</summary>
    public static class SpeechModelCatalog
    {
        public const string DefaultId = "large-v3-turbo-q8_0";

        /// <summary>The settings value for a model file the user picked themselves.</summary>
        public const string CustomId = "custom";

        private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

        // In display order. SHA-1 hashes are those listed in whisper.cpp's models/README.md; q8_0 has none there, so its
        // SHA-256 is the one Hugging Face reports for the file (X-Linked-Etag, recorded by the CI smoke test).
        public static IReadOnlyList<SpeechModelInfo> Models { get; } = new[]
        {
            new SpeechModelInfo(DefaultId, "ggml-large-v3-turbo-q8_0.bin", 874_188_075, null,
                "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1"),
            new SpeechModelInfo("large-v3-turbo", "ggml-large-v3-turbo.bin", 1_624_600_000, "4af2b29d7ec73d781377bfd1758ca957a807e941"),
            new SpeechModelInfo("large-v3-turbo-q5_0", "ggml-large-v3-turbo-q5_0.bin", 573_600_000, "e050f7970618a659205450ad97eb95a18d69c9ee"),
        };

        /// <summary>Where downloaded models are kept: local (not roaming) app data, since the files are large.</summary>
        public static string DefaultModelsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovaCore", "models");

        public static SpeechModelInfo? Find(string id)
        {
            foreach (SpeechModelInfo model in Models)
            {
                if (model.Id == id) return model;
            }
            return null;
        }

        /// <summary>The catalog model a settings value selects: null for a custom file; an unknown id means the default.</summary>
        public static SpeechModelInfo? Selected(string modelId) =>
            modelId == CustomId ? null : Find(modelId) ?? Find(DefaultId);

        public static Uri DownloadUrl(SpeechModelInfo model) => new(BaseUrl + model.FileName);

        /// <summary>The model file the settings select, or null if they select a custom file that was never chosen.</summary>
        public static string? ResolvePath(string modelId, string customPath, string modelsDirectory)
        {
            if (Selected(modelId) is { } model) return Path.Combine(modelsDirectory, model.FileName);
            return string.IsNullOrWhiteSpace(customPath) ? null : customPath;
        }
    }
}
