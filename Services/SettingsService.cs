using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using LayoutConverter.App.Models;

namespace LayoutConverter.App.Services
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
            catch { }
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
                MessageBox.Show($"Error saving settings: {ex.Message}", "MovaCore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStartupRegistration(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
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
            catch { }
        }
    }

    // Helper to avoid issues with File.WriteAllText in some environments
    internal static class FileExtensions
    {
        public static void WriteText(string path, string text)
        {
            File.WriteAllText(path, text);
        }
    }
}
