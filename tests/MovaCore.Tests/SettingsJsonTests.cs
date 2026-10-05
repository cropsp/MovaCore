using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovaCore.Models;
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

        // A settings file written by v1.1 knows nothing about the properties added since
        [Fact]
        public void V11File_WithoutTheNewProperties_LoadsWithDefaults()
        {
            const string json = """
                {
                  "TriggerKey": "VcPause",
                  "LaunchAtStartup": true,
                  "ShowNotifications": false,
                  "RestoreClipboard": false
                }
                """;

            AppSettings? settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(settings);
            Assert.Equal(KeyCode.VcPause, settings.TriggerKey);
            Assert.True(settings.LaunchAtStartup);
            Assert.False(settings.ShowNotifications);
            Assert.False(settings.RestoreClipboard);

            Assert.Equal(HotkeyModifiers.None, settings.TriggerModifiers);
            Assert.True(settings.SwitchLayout);
            Assert.True(settings.SelectConvertedText);
            Assert.False(settings.ConvertLastWord);
            Assert.Equal(CopyPasteKeys.CtrlCV, settings.CopyPasteKeys);
            Assert.NotNull(settings.ExcludedProcesses);
            Assert.Empty(settings.ExcludedProcesses);
            Assert.Equal(UiLanguage.Auto, settings.Language);
            Assert.Equal(new Hotkey(KeyCode.VcPause, HotkeyModifiers.None), settings.Trigger);
        }

        [Fact]
        public void EmptyObject_LoadsWithDefaults()
        {
            AppSettings? settings = JsonSerializer.Deserialize("{}", SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(settings);
            Assert.Equal(KeyCode.VcF10, settings.TriggerKey);
            Assert.Equal(HotkeyModifiers.None, settings.TriggerModifiers);
            Assert.False(settings.LaunchAtStartup);
            Assert.True(settings.ShowNotifications);
            Assert.True(settings.RestoreClipboard);
            Assert.True(settings.SwitchLayout);
            Assert.True(settings.SelectConvertedText);
            Assert.False(settings.ConvertLastWord);
            Assert.Equal(CopyPasteKeys.CtrlCV, settings.CopyPasteKeys);
            Assert.Empty(settings.ExcludedProcesses);
            Assert.Equal(UiLanguage.Auto, settings.Language);
            Assert.False(settings.SpeechEnabled);
            Assert.Equal(new Hotkey(KeyCode.VcScrollLock, HotkeyModifiers.None), settings.SpeechHotkey);
            Assert.Equal("large-v3-turbo-q8_0", settings.SpeechModel);
            Assert.Equal("", settings.SpeechCustomModelPath);
            Assert.Equal("auto", settings.SpeechLanguage);
            Assert.Null(settings.SpeechMicrophoneId);
            Assert.True(settings.SpeechUseGpu);
            Assert.True(settings.SpeechShowOverlay);
        }

        private static AppSettings NonDefaultSettings() => new()
        {
            TriggerKey = KeyCode.VcScrollLock,
            TriggerModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
            LaunchAtStartup = true,
            ShowNotifications = false,
            RestoreClipboard = false,
            SwitchLayout = false,
            SelectConvertedText = false,
            ConvertLastWord = true,
            CopyPasteKeys = CopyPasteKeys.CtrlInsertShiftInsert,
            ExcludedProcesses = new List<string> { "devenv", "Code" },
            Language = UiLanguage.Ukrainian,
            SpeechEnabled = true,
            SpeechKey = KeyCode.VcF9,
            SpeechModifiers = HotkeyModifiers.Alt,
            SpeechModel = "custom",
            SpeechCustomModelPath = @"D:\models\ggml-large-v3-turbo.bin",
            SpeechLanguage = "uk",
            SpeechMicrophoneId = "{0.0.1.00000000}.{a1b2c3}",
            SpeechUseGpu = false,
            SpeechShowOverlay = false,
        };

        [Fact]
        public void AllProperties_RoundTrip()
        {
            AppSettings original = NonDefaultSettings();

            string json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.AppSettings);
            AppSettings? restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(restored);
            Assert.Equal(KeyCode.VcScrollLock, restored.TriggerKey);
            Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, restored.TriggerModifiers);
            Assert.True(restored.LaunchAtStartup);
            Assert.False(restored.ShowNotifications);
            Assert.False(restored.RestoreClipboard);
            Assert.False(restored.SwitchLayout);
            Assert.False(restored.SelectConvertedText);
            Assert.True(restored.ConvertLastWord);
            Assert.Equal(CopyPasteKeys.CtrlInsertShiftInsert, restored.CopyPasteKeys);
            Assert.Equal(new[] { "devenv", "Code" }, restored.ExcludedProcesses);
            Assert.Equal(UiLanguage.Ukrainian, restored.Language);
            Assert.Equal(new Hotkey(KeyCode.VcScrollLock, HotkeyModifiers.Control | HotkeyModifiers.Shift), restored.Trigger);
            Assert.True(restored.SpeechEnabled);
            Assert.Equal(new Hotkey(KeyCode.VcF9, HotkeyModifiers.Alt), restored.SpeechHotkey);
            Assert.Equal("custom", restored.SpeechModel);
            Assert.Equal(@"D:\models\ggml-large-v3-turbo.bin", restored.SpeechCustomModelPath);
            Assert.Equal("uk", restored.SpeechLanguage);
            Assert.Equal("{0.0.1.00000000}.{a1b2c3}", restored.SpeechMicrophoneId);
            Assert.False(restored.SpeechUseGpu);
            Assert.False(restored.SpeechShowOverlay);
        }

        // Guards the round trip above: a property added to AppSettings must get a non-default value there
        [Fact]
        public void NonDefaultSettings_ChangeEveryStoredProperty()
        {
            var defaults = new AppSettings();
            AppSettings changed = NonDefaultSettings();

            foreach (PropertyInfo property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

                object? defaultValue = property.GetValue(defaults);
                object? changedValue = property.GetValue(changed);
                bool same = defaultValue is IEnumerable<string> defaultList && changedValue is IEnumerable<string> changedList
                    ? defaultList.SequenceEqual(changedList)
                    : Equals(defaultValue, changedValue);

                Assert.False(same, $"{property.Name} keeps its default value in NonDefaultSettings()");
            }
        }

        [Fact]
        public void TriggerModifiers_AreWrittenAsNames()
        {
            var settings = new AppSettings { TriggerModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift };

            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

            Assert.Contains("\"TriggerModifiers\": \"Control, Shift\"", json);
        }

        [Fact]
        public void TriggerModifiers_None_IsWrittenAsAName()
        {
            string json = JsonSerializer.Serialize(new AppSettings(), SettingsJsonContext.Default.AppSettings);

            Assert.Contains("\"TriggerModifiers\": \"None\"", json);
        }

        [Fact]
        public void TriggerModifiers_WrittenAsNames_AreRead()
        {
            const string json = "{ \"TriggerModifiers\": \"Alt, Win\" }";

            AppSettings? settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(settings);
            Assert.Equal(HotkeyModifiers.Alt | HotkeyModifiers.Win, settings.TriggerModifiers);
        }

        [Fact]
        public void EnumSettings_AreWrittenAsNames()
        {
            AppSettings settings = NonDefaultSettings();

            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

            Assert.Contains("\"CopyPasteKeys\": \"CtrlInsertShiftInsert\"", json);
            Assert.Contains("\"Language\": \"Ukrainian\"", json);
        }

        // Trigger is a view of TriggerKey and TriggerModifiers; storing it as well would let the three disagree
        [Fact]
        public void TriggerHelper_IsNotWrittenToJson()
        {
            var settings = new AppSettings { Trigger = new Hotkey(KeyCode.VcF9, HotkeyModifiers.Alt) };

            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

            using JsonDocument document = JsonDocument.Parse(json);
            List<string> names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            Assert.DoesNotContain("Trigger", names);
            Assert.Contains("TriggerKey", names);
            Assert.Contains("TriggerModifiers", names);
            Assert.Equal("VcF9", document.RootElement.GetProperty("TriggerKey").GetString());
            Assert.Equal("Alt", document.RootElement.GetProperty("TriggerModifiers").GetString());
        }

        [Fact]
        public void SpeechHotkeyHelper_IsNotWrittenToJson()
        {
            var settings = new AppSettings { SpeechHotkey = new Hotkey(KeyCode.VcF8, HotkeyModifiers.Control) };

            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

            using JsonDocument document = JsonDocument.Parse(json);
            List<string> names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            Assert.DoesNotContain("SpeechHotkey", names);
            Assert.Equal("VcF8", document.RootElement.GetProperty("SpeechKey").GetString());
            Assert.Equal("Control", document.RootElement.GetProperty("SpeechModifiers").GetString());
        }

        [Fact]
        public void TriggerHelper_InTheFile_IsIgnored()
        {
            const string json = "{ \"TriggerKey\": \"VcF9\", \"Trigger\": { \"Key\": \"VcF1\", \"Modifiers\": 1 } }";

            AppSettings? settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(settings);
            Assert.Equal(new Hotkey(KeyCode.VcF9, HotkeyModifiers.None), settings.Trigger);
        }

        [Fact]
        public void ExcludedProcesses_KeepOrderAndCase()
        {
            var settings = new AppSettings { ExcludedProcesses = new List<string> { "devenv", "Code", "WindowsTerminal.exe" } };

            string json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            AppSettings? restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

            Assert.NotNull(restored);
            Assert.Equal(new[] { "devenv", "Code", "WindowsTerminal.exe" }, restored.ExcludedProcesses);
        }
    }
}
