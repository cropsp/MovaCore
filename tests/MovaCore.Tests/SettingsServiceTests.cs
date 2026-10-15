using MovaCore.Models;
using MovaCore.Services;
using SharpHook.Data;
using Xunit;

namespace MovaCore.Tests
{
    public sealed class SettingsServiceTests : IDisposable
    {
        private const string ExePath = @"C:\apps\MovaCore\MovaCore.exe";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MovaCoreTests-" + Guid.NewGuid().ToString("N"));
        private readonly FakeStartupRegistration _startup = new();

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private SettingsService CreateService(string executablePath = ExePath) =>
            new(_startup, _directory, executablePath);

        private void WriteSettingsFile(string content)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "settings.json"), content);
        }

        [Fact]
        public void Load_MissingFile_ReturnsDefaults()
        {
            AppSettings settings = CreateService().Load();

            Assert.Equal(KeyCode.VcF10, settings.TriggerKey);
            Assert.True(settings.ShowNotifications);
            Assert.True(settings.RestoreClipboard);
            Assert.False(settings.LaunchAtStartup);
        }

        [Fact]
        public void IsFirstRun_UntilTheSettingsAreSaved()
        {
            SettingsService service = CreateService();

            service.Load();
            Assert.True(service.IsFirstRun);

            service.Save(new AppSettings());
            Assert.False(service.IsFirstRun);

            SettingsService nextStart = CreateService();
            nextStart.Load();
            Assert.False(nextStart.IsFirstRun);
        }

        // Settings that cannot be read are reset, but MovaCore has been set up before: no first steps again
        [Fact]
        public void IsFirstRun_NotWithAnUnreadableFile()
        {
            WriteSettingsFile("{ not json");
            SettingsService service = CreateService();

            service.Load();

            Assert.False(service.IsFirstRun);
        }

        [Fact]
        public void Clone_IsIndependent()
        {
            var settings = new AppSettings { SpeechEnabled = true, ExcludedProcesses = { "code" } };

            AppSettings copy = settings.Clone();
            copy.SpeechEnabled = false;
            copy.ExcludedProcesses.Add("devenv");

            Assert.True(settings.SpeechEnabled);
            Assert.Equal(new[] { "code" }, settings.ExcludedProcesses);
            Assert.Equal(new[] { "code", "devenv" }, copy.ExcludedProcesses);
            Assert.Equal(settings.SpeechHotkey, copy.SpeechHotkey);
        }

        [Fact]
        public void Save_ThenLoad_RoundTrips()
        {
            SettingsService service = CreateService();
            var original = new AppSettings
            {
                TriggerKey = KeyCode.VcScrollLock,
                LaunchAtStartup = true,
                ShowNotifications = false,
                RestoreClipboard = false
            };

            service.Save(original);
            AppSettings restored = service.Load();

            Assert.Equal(KeyCode.VcScrollLock, restored.TriggerKey);
            Assert.True(restored.LaunchAtStartup);
            Assert.False(restored.ShowNotifications);
            Assert.False(restored.RestoreClipboard);
        }

        [Fact]
        public void Load_CorruptFile_ReturnsDefaults()
        {
            WriteSettingsFile("{ not json");

            AppSettings settings = CreateService().Load();

            Assert.Equal(KeyCode.VcF10, settings.TriggerKey);
            Assert.True(settings.ShowNotifications);
            Assert.True(settings.RestoreClipboard);
            Assert.False(settings.LaunchAtStartup);
        }

        [Fact]
        public void Load_FileFromV1_DefaultsRestoreClipboardToTrue()
        {
            WriteSettingsFile("{ \"TriggerKey\": 121, \"LaunchAtStartup\": false, \"ShowNotifications\": false }");

            AppSettings settings = CreateService().Load();

            Assert.Equal(KeyCode.VcF10, settings.TriggerKey);
            Assert.False(settings.ShowNotifications);
            Assert.True(settings.RestoreClipboard);
        }

        [Fact]
        public void Save_LeavesOnlyTheSettingsFile()
        {
            SettingsService service = CreateService();

            // The second save replaces an existing file, which takes a different code path than the first.
            service.Save(new AppSettings());
            service.Save(new AppSettings { TriggerKey = KeyCode.VcPause });

            string[] files = Directory.GetFileSystemEntries(_directory);
            Assert.Equal(new[] { service.SettingsFilePath }, files);
            Assert.Equal(KeyCode.VcPause, service.Load().TriggerKey);
        }

        [Fact]
        public void Save_RegistersOrUnregistersAutostart()
        {
            SettingsService service = CreateService();

            service.Save(new AppSettings { LaunchAtStartup = true });

            Assert.Equal(new[] { ExePath }, _startup.RegisterCalls);
            Assert.Equal(0, _startup.UnregisterCalls);

            service.Save(new AppSettings { LaunchAtStartup = false });

            Assert.Equal(new[] { ExePath }, _startup.RegisterCalls);
            Assert.Equal(1, _startup.UnregisterCalls);
        }

        [Fact]
        public void Load_TakesLaunchAtStartupFromTheRegistration()
        {
            SettingsService service = CreateService();

            // The file claims autostart is on, but nothing is registered.
            WriteSettingsFile("{ \"LaunchAtStartup\": true }");
            Assert.False(service.Load().LaunchAtStartup);

            // The file claims it is off, but the entry exists.
            WriteSettingsFile("{ \"LaunchAtStartup\": false }");
            _startup.RegisteredPath = ExePath;
            Assert.True(service.Load().LaunchAtStartup);
        }

        [Fact]
        public void Load_UpdatesTheRegisteredPath_WhenTheExecutableMoved()
        {
            _startup.RegisteredPath = @"C:\old\MovaCore.exe";
            SettingsService service = CreateService(@"C:\new\MovaCore.exe");

            AppSettings settings = service.Load();

            Assert.True(settings.LaunchAtStartup);
            Assert.Equal(new[] { @"C:\new\MovaCore.exe" }, _startup.RegisterCalls);
            Assert.Equal(@"C:\new\MovaCore.exe", _startup.RegisteredPath);
        }

        // Running another copy (a debug build, a smoke test) must not take over the installed copy's autostart
        [Fact]
        public void Load_LeavesTheRegistrationAlone_WhenTheRegisteredCopyStillExists()
        {
            Directory.CreateDirectory(_directory);
            string installedCopy = Path.Combine(_directory, "MovaCore.exe");
            File.WriteAllText(installedCopy, "");
            _startup.RegisteredPath = installedCopy;

            AppSettings settings = CreateService(@"C:\devin\Debug\MovaCore.exe").Load();

            Assert.True(settings.LaunchAtStartup);
            Assert.Empty(_startup.RegisterCalls);
        }

        [Fact]
        public void Save_RefusesAutostart_WhenTheExecutablePathIsUnknown()
        {
            SettingsService service = CreateService(executablePath: "");

            Assert.Throws<InvalidOperationException>(() => service.Save(new AppSettings { LaunchAtStartup = true }));
            Assert.Empty(_startup.RegisterCalls);
        }

        [Fact]
        public void Load_LeavesTheRegistrationAlone_WhenThePathMatches()
        {
            _startup.RegisteredPath = ExePath.ToUpperInvariant();

            AppSettings settings = CreateService().Load();

            Assert.True(settings.LaunchAtStartup);
            Assert.Empty(_startup.RegisterCalls);
        }

        [Fact]
        public void Load_SurvivesRegistrationFailure()
        {
            _startup.ThrowOnGet = true;

            AppSettings settings = CreateService().Load();

            Assert.Equal(KeyCode.VcF10, settings.TriggerKey);
            Assert.Empty(_startup.RegisterCalls);
        }
    }

    internal sealed class FakeStartupRegistration : IStartupRegistration
    {
        public string? RegisteredPath { get; set; }

        public List<string> RegisterCalls { get; } = new();

        public int UnregisterCalls { get; private set; }

        public bool ThrowOnGet { get; set; }

        public string? GetRegisteredPath()
        {
            if (ThrowOnGet)
            {
                throw new InvalidOperationException("Registry is not available");
            }

            return RegisteredPath;
        }

        public void Register(string executablePath)
        {
            RegisterCalls.Add(executablePath);
            RegisteredPath = executablePath;
        }

        public void Unregister()
        {
            UnregisterCalls++;
            RegisteredPath = null;
        }
    }
}
