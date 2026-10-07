using System.Reflection;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    // Strings.Language is process-wide state: this is the only class that changes it, and it puts English back
    // after every test (xUnit creates a new instance, and disposes it, for each test).
    [Collection("Strings")]
    public class StringsTests : IDisposable
    {
        // The same text in both languages on purpose: language names and key combinations
        private static readonly string[] Untranslated =
        {
            nameof(Strings.LanguageEnglish),
            nameof(Strings.LanguageUkrainian),
            nameof(Strings.CopyPasteCtrlCV),
            nameof(Strings.CopyPasteCtrlInsert),
        };

        public StringsTests() => Strings.Language = UiLanguage.English;

        public void Dispose() => Strings.Language = UiLanguage.English;

        public static TheoryData<UiLanguage> Languages => new() { UiLanguage.English, UiLanguage.Ukrainian };

        private static PropertyInfo[] TextProperties() =>
            typeof(Strings)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(string))
                .ToArray();

        private static string Read(PropertyInfo property) => (string?)property.GetValue(null) ?? "";

        private static Dictionary<string, string> ReadAll(UiLanguage language)
        {
            Strings.Language = language;
            return TextProperties().ToDictionary(p => p.Name, Read);
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void EveryText_IsNotEmpty(UiLanguage language)
        {
            Strings.Language = language;

            PropertyInfo[] properties = TextProperties();
            Assert.True(properties.Length >= 30, "The reflection lookup found suspiciously few texts");
            foreach (PropertyInfo property in properties)
                Assert.False(string.IsNullOrWhiteSpace(Read(property)), $"{property.Name} is empty in {language}");
        }

        [Fact]
        public void MenuTexts_DifferBetweenLanguages()
        {
            Dictionary<string, string> english = ReadAll(UiLanguage.English);
            Dictionary<string, string> ukrainian = ReadAll(UiLanguage.Ukrainian);

            foreach (string name in new[]
            {
                nameof(Strings.MenuSettings), nameof(Strings.MenuPause), nameof(Strings.MenuAbout), nameof(Strings.MenuExit),
            })
            {
                Assert.NotEqual(english[name], ukrainian[name]);
            }
        }

        [Fact]
        public void EveryText_IsTranslated_ExceptTheKnownOnes()
        {
            Dictionary<string, string> english = ReadAll(UiLanguage.English);
            Dictionary<string, string> ukrainian = ReadAll(UiLanguage.Ukrainian);

            string[] same = english.Keys.Where(name => english[name] == ukrainian[name]).OrderBy(n => n).ToArray();

            Assert.Equal(Untranslated.OrderBy(n => n).ToArray(), same);
        }

        [Fact]
        public void UkrainianTexts_AreInCyrillic()
        {
            Dictionary<string, string> ukrainian = ReadAll(UiLanguage.Ukrainian);

            foreach ((string name, string text) in ukrainian.Where(p => !Untranslated.Contains(p.Key)))
                Assert.True(text.Any(c => c is >= 'Ѐ' and <= 'ӿ'), $"{name} has no Cyrillic letters");
        }

        [Fact]
        public void Auto_FallsBackToEnglish()
        {
            Dictionary<string, string> english = ReadAll(UiLanguage.English);
            Dictionary<string, string> auto = ReadAll(UiLanguage.Auto);

            Assert.Equal(english, auto);
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void TextsWithArguments_ContainThem(UiLanguage language)
        {
            Strings.Language = language;

            Assert.Contains("the reason", Strings.BalloonHookFailed("the reason"));
            Assert.Contains("the reason", Strings.BalloonUnexpectedError("the reason"));
            Assert.Contains("the reason", Strings.SettingsNotSaved("the reason"));
            Assert.Contains("1.2.3", Strings.AboutText("1.2.3", "the build"));
            Assert.Contains("the build", Strings.AboutText("1.2.3", "the build"));
            Assert.Contains("ScrollLock", Strings.BalloonModelReady("ScrollLock"));
            Assert.Contains("the reason", Strings.BalloonModelDownloadFailed("the reason"));
            Assert.Contains("the reason", Strings.SpeechModelDownloadFailed("the reason"));
            Assert.Contains("874", Strings.SpeechModelNotDownloaded("874 MB"));
            Assert.Contains("42", Strings.TrayTooltipDownloading(42));
            Assert.Contains("42", Strings.SpeechModelStillDownloading(42));
            Assert.Contains("the detail", Strings.SpeechErrorText(SpeechError.Failed, "the detail"));

            string downloading = Strings.SpeechModelDownloading("312 MB", "874 MB", 35);
            Assert.Contains("312 MB", downloading);
            Assert.Contains("874 MB", downloading);
            Assert.Contains("35", downloading);
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void EverySpeechError_HasAText(UiLanguage language)
        {
            Strings.Language = language;

            foreach (SpeechError error in Enum.GetValues<SpeechError>())
                Assert.False(string.IsNullOrWhiteSpace(Strings.SpeechErrorText(error, null)), error.ToString());
            foreach (ModelDownloadError error in Enum.GetValues<ModelDownloadError>())
                Assert.False(string.IsNullOrWhiteSpace(Strings.DownloadErrorText(error)), error.ToString());
        }

        [Fact]
        public void SpeechErrors_AreTranslated()
        {
            foreach (SpeechError error in Enum.GetValues<SpeechError>())
            {
                Strings.Language = UiLanguage.English;
                string english = Strings.SpeechErrorText(error, null);
                Strings.Language = UiLanguage.Ukrainian;
                Assert.NotEqual(english, Strings.SpeechErrorText(error, null));
            }
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void EverySpeechLanguage_HasAName(UiLanguage language)
        {
            Strings.Language = language;

            foreach (string code in SpeechLanguages.Codes)
            {
                string name = Strings.SpeechLanguageName(code);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.NotEqual(code, name);
            }
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void EveryCatalogModel_HasADistinctName(UiLanguage language)
        {
            Strings.Language = language;

            string[] names = SpeechModelCatalog.Models.Select(Strings.SpeechModelName).ToArray();
            Assert.Equal(names.Length, names.Distinct().Count());
            Assert.All(names, name => Assert.StartsWith("Large v3 Turbo", name));
        }

        [Theory]
        [InlineData(UiLanguage.English, 874_200_000, "874 MB")]
        [InlineData(UiLanguage.English, 1_624_600_000, "1.6 GB")]
        [InlineData(UiLanguage.Ukrainian, 573_600_000, "574 МБ")]
        [InlineData(UiLanguage.Ukrainian, 1_624_600_000, "1,6 ГБ")]
        public void FormatSize_UsesTheLanguagesUnitsAndSeparator(UiLanguage language, long bytes, string expected)
        {
            Strings.Language = language;

            Assert.Equal(expected, Strings.FormatSize(bytes));
        }

        // A discrete card's memory as Vulkan reports it: an 8 GB card has a little less than 8 GiB for itself
        [Theory]
        [InlineData(UiLanguage.English, true, 7.6, "NVIDIA GeForce RTX 4060 (8 GB)")]
        [InlineData(UiLanguage.Ukrainian, true, 1.9, "NVIDIA GeForce RTX 4060 (2 ГБ)")]
        [InlineData(UiLanguage.English, true, 0.0, "NVIDIA GeForce RTX 4060")]
        [InlineData(UiLanguage.English, false, 15.7, "NVIDIA GeForce RTX 4060 (integrated)")]
        [InlineData(UiLanguage.Ukrainian, false, 15.7, "NVIDIA GeForce RTX 4060 (вбудована)")]
        public void GpuName_GivesMemoryOrIntegrated(UiLanguage language, bool discrete, double gibibytes, string expected)
        {
            Strings.Language = language;

            var gpu = new GpuDevice(0, "NVIDIA GeForce RTX 4060", discrete, (ulong)(gibibytes * (1UL << 30)));
            Assert.Equal(expected, Strings.SpeechGpuName(gpu));
        }

        [Fact]
        public void BuildLabel_TellsTestBuildsFromReleasesAndLocalBuilds()
        {
            Strings.Language = UiLanguage.English;

            Assert.Equal("Test build 57 · 265ab8d · 2026-10-07", Strings.BuildLabel("57", "265ab8d", "2026-10-07", release: false));
            Assert.Equal("Build 57 · 265ab8d · 2026-10-07", Strings.BuildLabel("57", "265ab8d", "2026-10-07", release: true));
            Assert.Equal("Build 57 · 2026-10-07", Strings.BuildLabel("57", "", "2026-10-07", release: true));
            Assert.Equal("Local build · 2026-10-07", Strings.BuildLabel("", "", "2026-10-07", release: false));

            Strings.Language = UiLanguage.Ukrainian;
            Assert.Equal("Тестова збірка 57 · 265ab8d · 2026-10-07", Strings.BuildLabel("57", "265ab8d", "2026-10-07", release: false));
            Assert.Equal("Локальна збірка · 2026-10-07", Strings.BuildLabel("", "", "2026-10-07", release: false));
        }

        [Fact]
        public void FormatVersion_UsesMajorMinorBuild()
        {
            Assert.Equal("1.2.3", Strings.FormatVersion(new Version(1, 2, 3, 4)));
            Assert.Equal("1.2.0", Strings.FormatVersion(new Version(1, 2)));
            Assert.Equal("0.0.0", Strings.FormatVersion(null));
        }
    }
}
