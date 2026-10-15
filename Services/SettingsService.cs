using System;
using System.IO;
using System.Text.Json;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Loads and saves <see cref="AppSettings"/> as JSON and keeps the Windows autostart entry in sync.
    /// The registry is the source of truth for <see cref="AppSettings.LaunchAtStartup"/>, so the checkbox shows the real state.
    /// </summary>
    public sealed class SettingsService
    {
        private const string FileName = "settings.json";

        private readonly IStartupRegistration _startup;
        private readonly string _directory;
        private readonly string _executablePath;

        /// <param name="startup">Where the autostart entry lives.</param>
        /// <param name="settingsDirectory">Folder of settings.json; defaults to %APPDATA%\MovaCore.</param>
        /// <param name="executablePath">Path registered for autostart; defaults to the running executable.</param>
        public SettingsService(IStartupRegistration startup, string? settingsDirectory = null, string? executablePath = null)
        {
            _startup = startup;
            _directory = settingsDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MovaCore");
            _executablePath = executablePath ?? Environment.ProcessPath ?? "";
            SettingsFilePath = Path.Combine(_directory, FileName);
        }

        public string SettingsFilePath { get; }

        /// <summary>
        /// Whether <see cref="Load"/> found no settings file: MovaCore runs for the first time (or was never set up), so
        /// it shows its first steps. Saving once ends it.
        /// </summary>
        public bool IsFirstRun { get; private set; }

        /// <summary>Returns the saved settings, or defaults if there are none or they cannot be read. Never throws.</summary>
        public AppSettings Load()
        {
            IsFirstRun = !File.Exists(SettingsFilePath);
            AppSettings settings = ReadFile();
            SyncAutostart(settings);
            return settings;
        }

        /// <summary>Writes the settings and updates the autostart entry. Throws on failure; the caller reports it.</summary>
        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(_directory);

            // Write next to the target and swap it in, so a crash never leaves a half-written settings.json.
            string tempPath = SettingsFilePath + ".tmp";
            try
            {
                string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
                File.WriteAllText(tempPath, json);
                if (File.Exists(SettingsFilePath))
                {
                    File.Replace(tempPath, SettingsFilePath, null);
                }
                else
                {
                    File.Move(tempPath, SettingsFilePath);
                }
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }

            IsFirstRun = false;

            if (settings.LaunchAtStartup)
            {
                if (_executablePath.Length == 0)
                    throw new InvalidOperationException("The executable path is unknown, so autostart cannot be set up.");
                _startup.Register(_executablePath);
            }
            else
            {
                _startup.Unregister();
            }
        }

        private AppSettings ReadFile()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not load settings, using defaults", ex);
            }

            return new AppSettings();
        }

        private void SyncAutostart(AppSettings settings)
        {
            try
            {
                string? registered = _startup.GetRegisteredPath();
                settings.LaunchAtStartup = registered != null;

                // The registered executable is gone (moved or replaced by an update): point the entry at this copy.
                // If it still exists, another copy (e.g. a debug build) is running, and the entry stays as it is.
                if (registered != null
                    && _executablePath.Length > 0
                    && !string.Equals(registered.Trim(), _executablePath.Trim(), StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(registered))
                {
                    _startup.Register(_executablePath);
                    AppLog.Info("Autostart entry updated to the current executable path");
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not check the autostart entry", ex);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort: the original failure is the one worth reporting.
            }
        }
    }
}
