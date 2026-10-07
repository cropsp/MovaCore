using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public sealed class DictationHistoryTests : IDisposable
    {
        private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0);

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MovaCoreTests-" + Guid.NewGuid().ToString("N"));

        private string FilePath => Path.Combine(_directory, "history.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Fact]
        public void KeepsTheLastPhrases_NewestFirst()
        {
            var history = new DictationHistory(FilePath);
            for (int i = 1; i <= DictationHistory.Capacity + 2; i++) history.Add($"Фраза {i}.", pasted: true, Noon.AddMinutes(i));

            IReadOnlyList<DictationEntry> entries = history.Entries;

            Assert.Equal(DictationHistory.Capacity, entries.Count);
            Assert.Equal($"Фраза {DictationHistory.Capacity + 2}.", entries[0].Text);
            Assert.Equal("Фраза 3.", entries[^1].Text);
        }

        // In memory only unless the user asks: the promise that nothing said is saved stays true
        [Fact]
        public void WritesNothingToDisk_UnlessAskedTo()
        {
            var history = new DictationHistory(FilePath);
            history.SetSaveToDisk(false);

            history.Add("Привіт.", pasted: false, Noon);

            Assert.Single(history.Entries);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void SavedPhrases_SurviveARestart()
        {
            var history = new DictationHistory(FilePath);
            history.SetSaveToDisk(true);
            history.Add("Перша.", pasted: true, Noon);
            history.Add("Друга.", pasted: false, Noon.AddMinutes(1));

            var restarted = new DictationHistory(FilePath);
            restarted.SetSaveToDisk(true);

            Assert.Equal(new[] { "Друга.", "Перша." }, restarted.Entries.Select(entry => entry.Text));
            Assert.False(restarted.Entries[0].Pasted);
            Assert.Equal(Noon.AddMinutes(1), restarted.Entries[0].Time);
        }

        // Turned on during a session: what is in memory goes to the file
        [Fact]
        public void TurnedOnLater_SavesWhatIsInMemory()
        {
            var history = new DictationHistory(FilePath);
            history.Add("Раніше.", pasted: true, Noon);

            history.SetSaveToDisk(true);

            var restarted = new DictationHistory(FilePath);
            restarted.SetSaveToDisk(true);
            Assert.Equal("Раніше.", Assert.Single(restarted.Entries).Text);
        }

        [Fact]
        public void TurnedOff_DeletesTheFileButKeepsTheSession()
        {
            var history = new DictationHistory(FilePath);
            history.SetSaveToDisk(true);
            history.Add("Привіт.", pasted: true, Noon);

            history.SetSaveToDisk(false);

            Assert.False(File.Exists(FilePath));
            Assert.Single(history.Entries);
        }

        [Fact]
        public void Clear_EmptiesTheMenuAndTheFile()
        {
            var history = new DictationHistory(FilePath);
            history.SetSaveToDisk(true);
            history.Add("Привіт.", pasted: true, Noon);

            history.Clear();

            Assert.Empty(history.Entries);
            Assert.False(File.Exists(FilePath));
        }

        [Fact]
        public void UnreadableFile_StartsEmpty()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(FilePath, "{ not json");
            var history = new DictationHistory(FilePath);

            history.SetSaveToDisk(true);

            Assert.Empty(history.Entries);
        }

        [Theory]
        [InlineData(true, "12:00  Купи хліб.")]
        [InlineData(false, "12:00  Купи хліб. (not pasted)")]
        public void Label_HasTheTimeAndWhetherItWasPasted(bool pasted, string expected)
        {
            Strings.Language = UiLanguage.English;
            Assert.Equal(expected, Strings.HistoryEntryLabel(new DictationEntry(Noon, "Купи хліб.", pasted)));
        }

        [Fact]
        public void Label_ShortensALongPhrase()
        {
            Strings.Language = UiLanguage.English;
            string label = Strings.HistoryEntryLabel(new DictationEntry(Noon, new string('а', 80), true));

            Assert.EndsWith("…", label);
            Assert.Equal("12:00  ".Length + 50, label.Length);
        }
    }
}
