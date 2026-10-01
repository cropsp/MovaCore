using System.Reflection;
using MovaCore.Models;
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
            Assert.Contains("1.2.3", Strings.AboutText("1.2.3"));
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
