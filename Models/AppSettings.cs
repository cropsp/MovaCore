using System;
using System.Text.Json.Serialization;
using SharpHook.Data;

namespace LayoutConverter.App.Models
{
    public class AppSettings
    {
        public KeyCode TriggerKey { get; set; } = KeyCode.VcF10;
        public bool LaunchAtStartup { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
    }

    // Keys are stored by name: SharpHook 8 renumbers KeyCode, and names keep settings.json stable across upgrades.
    // Numeric values written by v1.0 still load, because the string enum converter also accepts integers.
    [JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(AppSettings))]
    internal partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
