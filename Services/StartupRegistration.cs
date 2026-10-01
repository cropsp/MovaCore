using System;
using Microsoft.Win32;

namespace MovaCore.Services
{
    /// <summary>
    /// Registers MovaCore in the current user's Run key, so Windows starts it at sign-in. Exceptions propagate to the
    /// caller, which decides whether a failure matters.
    /// </summary>
    public sealed class StartupRegistration : IStartupRegistration
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MovaCore";

        public string? GetRegisteredPath()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim();
            if (value.StartsWith('"'))
            {
                int end = value.IndexOf('"', 1);
                value = end > 0 ? value.Substring(1, end - 1) : value.Substring(1);
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public void Register(string executablePath)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, $"\"{executablePath}\"");
        }

        public void Unregister()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
