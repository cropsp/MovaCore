using SharpHook.Data;

namespace MovaCore.Models
{
    public class AppSettings
    {
        public KeyCode TriggerKey { get; set; } = KeyCode.VcF10;
        public bool LaunchAtStartup { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;

        /// <summary>Put the user's previous clipboard content back after converting.</summary>
        public bool RestoreClipboard { get; set; } = true;
    }
}
