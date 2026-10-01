using System.Text.Json.Serialization;

namespace MovaCore.Models
{
    // Keys are stored by name: SharpHook 8 renumbers KeyCode, and names keep settings.json stable across upgrades.
    // Numeric values written by v1.0 still load, because the string enum converter also accepts integers.
    [JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(AppSettings))]
    internal partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
