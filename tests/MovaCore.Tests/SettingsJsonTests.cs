using System.Text.Json;
using LayoutConverter.App.Models;
using SharpHook.Data;
using Xunit;

namespace MovaCore.Tests
{
    public class SettingsJsonTests
    {
        [Fact]
        public void TriggerKey_IsWrittenByName()
        {
            string json = JsonSerializer.Serialize(new AppSettings { TriggerKey = KeyCode.VcPause }, SettingsJsonContext.Default.AppSettings);

            Assert.Contains("\"TriggerKey\": \"VcPause\"", json);
        }

        // v1.0 wrote the key as a number (121 = VcF10 in SharpHook 5.3 and 7.x); such files must keep working.
        [Fact]
        public void NumericTriggerKey_FromV1_IsStillRead()
        {
            const string json = "{ \"TriggerKey\": 121, \"LaunchAtStartup\": true, \"ShowNotifications\": false }";

            AppSettings? settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(settings);
            Assert.Equal(KeyCode.VcF10, settings.TriggerKey);
            Assert.True(settings.LaunchAtStartup);
            Assert.False(settings.ShowNotifications);
        }

        [Fact]
        public void Settings_RoundTrip()
        {
            var original = new AppSettings { TriggerKey = KeyCode.VcScrollLock, LaunchAtStartup = true, ShowNotifications = false };

            string json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.AppSettings);
            AppSettings? restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(restored);
            Assert.Equal(original.TriggerKey, restored.TriggerKey);
            Assert.Equal(original.LaunchAtStartup, restored.LaunchAtStartup);
            Assert.Equal(original.ShowNotifications, restored.ShowNotifications);
        }
    }
}
