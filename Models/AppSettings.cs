using System;
using System.Text.Json.Serialization;
using SharpHook.Native;

namespace LayoutConverter.App.Models
{
    public class AppSettings
    {
        public KeyCode TriggerKey { get; set; } = KeyCode.VcF10;
        public bool LaunchAtStartup { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(AppSettings))]
    internal partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
