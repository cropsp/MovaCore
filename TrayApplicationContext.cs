using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;
using LayoutConverter.App.Services;

namespace LayoutConverter.App
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly IHotkeyService _hotkeyService;
        private readonly HotkeyOrchestrator _orchestrator;

        public TrayApplicationContext(
            IHotkeyService hotkeyService,
            HotkeyOrchestrator orchestrator)
        {
            _hotkeyService = hotkeyService;
            _orchestrator = orchestrator;

            // Initialize NotifyIcon
            _notifyIcon = new NotifyIcon
            {
                Text = "MovaCore - Layout Converter (F10)",
                ContextMenuStrip = CreateContextMenu(),
                Visible = true
            };

            // Set Icon from Resources with proper scaling for tray
            SetApplicationIcon();

            // Subscribe to debug notifications
            _orchestrator.ConversionCompleted += OnConversionCompleted;

            // Start Hotkey Service
            _hotkeyService.Start();
            _hotkeyService.HotkeyTriggered += OnHotkeyTriggered;
        }

        private void SetApplicationIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "mouse_icon.png");
                if (File.Exists(iconPath))
                {
                    using (var bitmap = new Bitmap(iconPath))
                    {
                        // Важливо: для системного трея картинка має бути 32x32
                        // Якщо ми просто даємо велику картинку, Windows її жахливо стискає (утворюється "абракадабра")
                        using (var resizedIcon = new Bitmap(32, 32))
                        {
                            using (var g = Graphics.FromImage(resizedIcon))
                            {
                                // Використовуємо найкращу якість згладжування
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(bitmap, 0, 0, 32, 32);
                            }
                            
                            IntPtr hIcon = resizedIcon.GetHicon();
                            _notifyIcon.Icon = Icon.FromHandle(hIcon);
                        }
                    }
                }
                else
                {
                    _notifyIcon.Icon = SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }

        private void OnConversionCompleted(object? sender, string message)
        {
            if (!string.IsNullOrEmpty(message))
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
            MessageBox.Show("MovaCore v1.0\nSettings will be available in the next update.", "MovaCore", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                _orchestrator.ConversionCompleted -= OnConversionCompleted;
                _notifyIcon?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
