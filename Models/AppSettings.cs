using System.Collections.Generic;
using System.Text.Json.Serialization;
using SharpHook.Data;

namespace MovaCore.Models
{
    public class AppSettings
    {
        public KeyCode TriggerKey { get; set; } = KeyCode.VcF10;

        /// <summary>Modifiers that must be held together with <see cref="TriggerKey"/>; none by default.</summary>
        public HotkeyModifiers TriggerModifiers { get; set; } = HotkeyModifiers.None;

        public bool LaunchAtStartup { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;

        /// <summary>Put the user's previous clipboard content back after converting.</summary>
        public bool RestoreClipboard { get; set; } = true;

        /// <summary>Switch the target window to the keyboard layout of the converted text.</summary>
        public bool SwitchLayout { get; set; } = true;

        /// <summary>Select the converted text again, so that a second press converts it back.</summary>
        public bool SelectConvertedText { get; set; } = true;

        /// <summary>When nothing is selected, select the word left of the caret and convert it (off: see the plan).</summary>
        public bool ConvertLastWord { get; set; } = false;

        public CopyPasteKeys CopyPasteKeys { get; set; } = CopyPasteKeys.CtrlCV;

        /// <summary>Process names (with or without ".exe") in which the hotkey is left to the application.</summary>
        public List<string> ExcludedProcesses { get; set; } = new();

        public UiLanguage Language { get; set; } = UiLanguage.Auto;

        [JsonIgnore]
        public Hotkey Trigger
        {
            get => new(TriggerKey, TriggerModifiers);
            set
            {
                TriggerKey = value.Key;
                TriggerModifiers = value.Modifiers;
            }
        }
    }
}
