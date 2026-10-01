using System;
using System.Drawing;
using System.Threading;
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
        private readonly SynchronizationContext _uiContext;
        private AppSettings _currentSettings;

        public TrayApplicationContext(
            IHotkeyService hotkeyService,
            HotkeyOrchestrator orchestrator,
            SettingsService settingsService)
        {
            _hotkeyService = hotkeyService;
            _orchestrator = orchestrator;
            _settingsService = settingsService;

            // Load and apply settings
            _currentSettings = _settingsService.Load();
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

            // The events below arrive on worker threads, but NotifyIcon may only be used on this (UI) thread
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _orchestrator.ConversionFailed += OnConversionFailed;
            _hotkeyService.HookFailed += OnHookFailed;

            // Start Hotkey Service
            _hotkeyService.HotkeyTriggered += OnHotkeyTriggered;
            _hotkeyService.Start();
        }

        private void ApplySettings()
        {
            _hotkeyService.SetTriggerKey(_currentSettings.TriggerKey);
            _orchestrator.RestoreClipboard = _currentSettings.RestoreClipboard;
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
            _uiContext.Post(_ =>
            {
                if (_currentSettings.ShowNotifications && !string.IsNullOrEmpty(message))
                {
                    _notifyIcon.ShowBalloonTip(3000, "MovaCore", message, ToolTipIcon.Info);
                }
            }, null);
        }

        private void OnHookFailed(object? sender, Exception error)
        {
            // Shown even with notifications off: without the hook the hotkey silently does nothing
            _uiContext.Post(_ => _notifyIcon.ShowBalloonTip(
                5000,
                "MovaCore",
                $"The keyboard hook stopped, so the hotkey does not work. Please restart MovaCore. ({error.Message})",
                ToolTipIcon.Error), null);
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
                    ApplySettings();

                    try
                    {
                        _settingsService.Save(_currentSettings);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Could not save settings", ex);
                        MessageBox.Show(
                            $"The settings are applied but could not be saved: {ex.Message}",
                            "MovaCore",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

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
                _hotkeyService.HookFailed -= OnHookFailed;
                _notifyIcon?.Dispose();
                _trayIcon?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
