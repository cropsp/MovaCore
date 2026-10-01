using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using MovaCore.Models;

namespace MovaCore.Services
{
    public class SettingsService
    {
        private readonly string _settingsFilePath;
        private const string AppName = "MovaCore";

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, AppName);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not load settings, using defaults", ex);
            }
            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
                File.WriteAllText(_settingsFilePath, json);

                UpdateStartupRegistration(settings.LaunchAtStartup);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not save settings", ex);
                MessageBox.Show($"Error saving settings: {ex.Message}", "MovaCore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStartupRegistration(bool enable)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not update the autostart registry entry", ex);
            }
        }
    }
}
