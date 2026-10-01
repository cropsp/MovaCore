using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class ProcessNamesTests
    {
        [Theory]
        [InlineData(" Code.EXE ", "code")]
        [InlineData("devenv", "devenv")]
        [InlineData("my.app.exe", "my.app")]
        [InlineData("Notepad.exe", "notepad")]
        [InlineData("WindowsTerminal", "windowsterminal")]
        public void Normalize_TrimsDropsExeAndLowercases(string name, string expected)
        {
            Assert.Equal(expected, ProcessNames.Normalize(name));
        }

        // Only a trailing ".exe" is a file extension; ".exe" in the middle of a name or no dot at all is kept
        [Theory]
        [InlineData("exe", "exe")]
        [InlineData("my.exe.app", "my.exe.app")]
        [InlineData("code.exe.exe", "code.exe")]
        [InlineData("app.dll", "app.dll")]
        public void Normalize_OnlyDropsATrailingExeExtension(string name, string expected)
        {
            Assert.Equal(expected, ProcessNames.Normalize(name));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(".exe")]
        [InlineData(" .EXE ")]
        public void Normalize_NothingButAnExtension_IsEmpty(string name)
        {
            Assert.Equal(string.Empty, ProcessNames.Normalize(name));
        }

        [Fact]
        public void NormalizeAll_MergesDuplicatesAndDropsBlankNames()
        {
            HashSet<string> names = ProcessNames.NormalizeAll(new[] { "Code", "code.exe", " ", "DEVENV.exe" });

            Assert.Equal(2, names.Count);
            Assert.Contains("code", names);
            Assert.Contains("devenv", names);
        }

        [Fact]
        public void NormalizeAll_DropsNamesThatAreOnlyAnExtension()
        {
            HashSet<string> names = ProcessNames.NormalizeAll(new[] { ".exe", "", "Code" });

            Assert.Equal(new[] { "code" }, names);
        }

        [Fact]
        public void NormalizeAll_NoNames_IsEmpty()
        {
            Assert.Empty(ProcessNames.NormalizeAll(Array.Empty<string>()));
        }

        // The set compares ordinally, so callers normalize the name they look up as well
        [Fact]
        public void NormalizeAll_ResultIsLookedUpWithTheNormalizedName()
        {
            HashSet<string> names = ProcessNames.NormalizeAll(new[] { "Code.exe" });

            Assert.Contains(ProcessNames.Normalize("CODE"), names);
            Assert.DoesNotContain("Code", names);
        }
    }
}
