using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;
using MovaCore.UI;

namespace MovaCore
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon? _trayIcon;
        private readonly IHotkeyService _hotkeyService;
        private readonly HotkeyOrchestrator _orchestrator;
        private readonly SettingsService _settingsService;
        private AppSettings _currentSettings;

        public TrayApplicationContext(
            IHotkeyService hotkeyService,
            HotkeyOrchestrator orchestrator)
        {
            _hotkeyService = hotkeyService;
            _orchestrator = orchestrator;
            _settingsService = new SettingsService();

            // Load and apply settings
            _currentSettings = _settingsService.LoadSettings();
            ApplySettings();

            // Initialize NotifyIcon
            _trayIcon = LoadTrayIcon();
            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon ?? SystemIcons.Application,
                Text = "MovaCore - Layout Converter",
                ContextMenuStrip = CreateContextMenu(),
                Visible = true
            };

            // Subscribe to debug notifications
            _orchestrator.ConversionFailed += OnConversionFailed;

            // Start Hotkey Service
            _hotkeyService.HotkeyTriggered += OnHotkeyTriggered;
            _hotkeyService.Start();
        }

        private void ApplySettings()
        {
            _hotkeyService.SetTriggerKey(_currentSettings.TriggerKey);
        }

        private static Icon? LoadTrayIcon()
        {
            try
            {
                return AppResources.LoadIcon(SystemInformation.SmallIconSize);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not load the tray icon", ex);
                return null;
            }
        }

        private void OnConversionFailed(object? sender, string message)
        {
            if (_currentSettings.ShowNotifications && !string.IsNullOrEmpty(message))
            {
                _notifyIcon.ShowBalloonTip(3000, "MovaCore", message, ToolTipIcon.Info);
            }
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            menu.Items.Add("Settings", null, (s, e) => ShowSettings());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (s, e) => Exit());

            return menu;
        }

        private void OnHotkeyTriggered(object? sender, EventArgs e)
        {
            Task.Run(async () => await _orchestrator.ExecuteConversionAsync());
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(_currentSettings))
            {
                if (form.ShowDialog() == DialogResult.OK && form.UpdatedSettings is { } updatedSettings)
                {
                    _currentSettings = updatedSettings;
                    _settingsService.SaveSettings(_currentSettings);
                    ApplySettings();

                    if (_currentSettings.ShowNotifications)
                    {
                        _notifyIcon.ShowBalloonTip(2000, "MovaCore", "Settings saved and applied successfully!", ToolTipIcon.Info);
                    }
                }
            }
        }

        private void Exit()
        {
            _notifyIcon.Visible = false;
            _hotkeyService.Stop();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _orchestrator.ConversionFailed -= OnConversionFailed;
                _notifyIcon?.Dispose();
                _trayIcon?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
