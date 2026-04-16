using System;
using System.Drawing;
using System.Windows.Forms;
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
                Icon = SystemIcons.Application, // Replace with AppIcon.ico in production
                Text = "MovaCore - Layout Converter",
                ContextMenuStrip = CreateContextMenu(),
                Visible = true
            };

            // Start Hotkey Service
            _hotkeyService.Start();
            _hotkeyService.HotkeyTriggered += OnHotkeyTriggered;
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            menu.Items.Add("Settings", null, (s, e) => ShowSettings());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (s, e) => Exit());

            return menu;
        }

        private void OnHotkeyTriggered(object sender, EventArgs e)
        {
            // Run orchestrator logic
            _orchestrator.ExecuteConversionAsync();
        }

        private void ShowSettings()
        {
            MessageBox.Show("Settings will be implemented here.", "MovaCore Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                _notifyIcon?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
