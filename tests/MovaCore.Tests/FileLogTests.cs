using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class FileLogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "MovaCoreTests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (Exception)
            {
                // Best-effort cleanup: the directory may already be gone or briefly locked.
            }
        }

        [Fact]
        public void Info_WritesTimestampedLine()
        {
            var log = new FileLog(_dir);

            log.Info("hello");

            var lines = File.ReadAllLines(log.FilePath);
            var line = Assert.Single(lines);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO \] hello$", line);
        }

        [Fact]
        public void Error_IncludesExceptionDetails()
        {
            var log = new FileLog(_dir);

            log.Error("failed", new InvalidOperationException("boom"));

            var text = File.ReadAllText(log.FilePath);
            Assert.Contains("[ERROR] failed", text);
            Assert.Contains("System.InvalidOperationException", text);
            Assert.Contains("boom", text);
        }

        [Fact]
        public void Constructor_CreatesMissingDirectory()
        {
            var nested = Path.Combine(_dir, "a", "b");

            _ = new FileLog(nested);

            Assert.True(Directory.Exists(nested));
        }

        [Fact]
        public void Rotation_MovesFullFileAside()
        {
            var log = new FileLog(_dir, maxBytes: 200);

            for (int i = 0; i < 20; i++)
                log.Info(new string('x', 40));

            var rotated = Path.Combine(_dir, "movacore.1.log");
            Assert.True(File.Exists(rotated));
            Assert.True(new FileInfo(log.FilePath).Length < 200 + 100);

            var names = Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "movacore.1.log", "movacore.log" }, names);
        }

        [Fact]
        public void Write_DoesNotThrow_WhenDirectoryIsDeleted()
        {
            var log = new FileLog(_dir);
            Directory.Delete(_dir, recursive: true);

            var exception = Record.Exception(() =>
            {
                log.Info("info after delete");
                log.Error("error after delete", new InvalidOperationException("boom"));
            });

            Assert.Null(exception);
        }

        [Fact]
        public void ConcurrentWrites_AreNotLost()
        {
            var log = new FileLog(_dir);

            Parallel.For(0, 200, i => log.Info($"line {i}"));

            var lines = File.ReadAllLines(log.FilePath);
            Assert.Equal(200, lines.Length);

            var messages = lines.Select(l => Regex.Match(l, @"\[INFO \] (line \d+)$")).ToList();
            Assert.All(messages, m => Assert.True(m.Success));

            var expected = Enumerable.Range(0, 200).Select(i => $"line {i}").ToHashSet();
            var actual = messages.Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(200, actual.Distinct().Count());
            Assert.True(expected.SetEquals(actual));
        }
    }
}
