using System;
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

        /// <summary>
        /// Select the converted text again, so that a second press converts it back. Off by default since v1.3: typing
        /// right after a conversion replaced the selected text. Until v1.2 this was SelectConvertedText (on by default);
        /// the new name lets every settings file start with it off.
        /// </summary>
        public bool ReselectConvertedText { get; set; } = false;

        /// <summary>When nothing is selected, select the word left of the caret and convert it (off: see the plan).</summary>
        public bool ConvertLastWord { get; set; } = false;

        public CopyPasteKeys CopyPasteKeys { get; set; } = CopyPasteKeys.CtrlCV;

        /// <summary>Process names (with or without ".exe") in which the hotkey is left to the application.</summary>
        public List<string> ExcludedProcesses { get; set; } = new();

        public UiLanguage Language { get; set; } = UiLanguage.Auto;

        /// <summary>Hold-to-talk dictation; off until the user turns it on, since it needs a speech model.</summary>
        public bool SpeechEnabled { get; set; } = false;

        public KeyCode SpeechKey { get; set; } = KeyCode.VcScrollLock;
        public HotkeyModifiers SpeechModifiers { get; set; } = HotkeyModifiers.None;

        /// <summary>A <see cref="Services.SpeechModelCatalog"/> id, or "custom" for <see cref="SpeechCustomModelPath"/>.</summary>
        public string SpeechModel { get; set; } = Services.SpeechModelCatalog.DefaultId;

        public string SpeechCustomModelPath { get; set; } = "";

        /// <summary>A Whisper language code, e.g. "uk", or "auto" to detect it.</summary>
        public string SpeechLanguage { get; set; } = SpeechLanguages.Auto;

        /// <summary>The Windows audio endpoint ID of the microphone; null for the Windows default.</summary>
        public string? SpeechMicrophoneId { get; set; }

        /// <summary>Transcribe on the GPU through Vulkan where available (x64 only); false: on the processor only.</summary>
        public bool SpeechUseGpu { get; set; } = true;

        /// <summary>The graphics card's name as Vulkan gives it; null to choose one automatically (a discrete card first).</summary>
        public string? SpeechGpu { get; set; }

        /// <summary>
        /// Encode short phrases with a shorter audio context (<see cref="Services.WhisperAudioContext"/>): several times
        /// faster, possibly a little less accurate. Experimental.
        /// </summary>
        public bool SpeechFastRecognition { get; set; } = true;

        /// <summary>When the speech model is freed from memory after the last dictation (as in Handy, 5 minutes by default).</summary>
        public ModelUnloadTimeout SpeechModelUnload { get; set; } = ModelUnloadTimeout.After5Minutes;

        /// <summary>Show the small recording indicator near the bottom of the screen.</summary>
        public bool SpeechShowOverlay { get; set; } = true;

        /// <summary>
        /// Keep the last dictated phrases in a file, so that the tray menu offers them after a restart too; off: in memory
        /// only (see <see cref="Services.DictationHistory"/>).
        /// </summary>
        public bool SpeechHistoryOnDisk { get; set; } = false;

        /// <summary>A copy that can be changed without touching this one.</summary>
        public AppSettings Clone()
        {
            var copy = (AppSettings)MemberwiseClone();
            copy.ExcludedProcesses = new List<string>(ExcludedProcesses);
            return copy;
        }

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

        /// <summary>How long the speech model stays loaded after a dictation; null: as long as dictation is on.</summary>
        [JsonIgnore]
        public TimeSpan? SpeechModelUnloadDelay => SpeechModelUnload switch
        {
            ModelUnloadTimeout.Never => null,
            ModelUnloadTimeout.Immediately => TimeSpan.Zero,
            ModelUnloadTimeout.After2Minutes => TimeSpan.FromMinutes(2),
            ModelUnloadTimeout.After5Minutes => TimeSpan.FromMinutes(5),
            ModelUnloadTimeout.After10Minutes => TimeSpan.FromMinutes(10),
            ModelUnloadTimeout.After15Minutes => TimeSpan.FromMinutes(15),
            ModelUnloadTimeout.After1Hour => TimeSpan.FromHours(1),
            _ => TimeSpan.FromMinutes(5), // a number written by hand that is not a choice
        };

        [JsonIgnore]
        public Hotkey SpeechHotkey
        {
            get => new(SpeechKey, SpeechModifiers);
            set
            {
                SpeechKey = value.Key;
                SpeechModifiers = value.Modifiers;
            }
        }
    }
}
