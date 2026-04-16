using System;
using System.Drawing;
using System.Windows.Forms;
using SharpHook.Native;
using LayoutConverter.App.Models;

namespace LayoutConverter.App.UI
{
    public class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private KeyCode _tempKeyCode;
        private bool _isRecording = false;

        private Label _hotkeyLabel;
        private Button _recordButton;
        private CheckBox _startupCheckBox;
        private CheckBox _notifyCheckBox;
        private Button _saveButton;
        private Button _cancelButton;

        public AppSettings UpdatedSettings { get; private set; }

        public SettingsForm(AppSettings currentSettings)
        {
            _settings = currentSettings;
            _tempKeyCode = _settings.TriggerKey;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "MovaCore Settings";
            this.Size = new Size(350, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;

            // Logo Section
            var logoBox = new PictureBox
            {
                Location = new Point(125, 20),
                Size = new Size(100, 100),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "mouse_icon.png");
                if (System.IO.File.Exists(iconPath)) logoBox.Image = Image.FromFile(iconPath);
            }
            catch { }
            this.Controls.Add(logoBox);

            var titleLabel = new Label
            {
                Text = "MovaCore v1.0.0",
                Location = new Point(0, 125),
                Size = new Size(350, 25),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            this.Controls.Add(titleLabel);

            // Hotkey Section
            var hotkeyGroup = new GroupBox
            {
                Text = "Keyboard Shortcut",
                Location = new Point(20, 160),
                Size = new Size(300, 80)
            };
            this.Controls.Add(hotkeyGroup);

            _hotkeyLabel = new Label
            {
                Text = $"Current: {_tempKeyCode.ToString().Replace("Vc", "")}",
                Location = new Point(15, 35),
                Size = new Size(150, 25),
                TextAlign = ContentAlignment.MiddleLeft
            };
            hotkeyGroup.Controls.Add(_hotkeyLabel);

            _recordButton = new Button
            {
                Text = "Change",
                Location = new Point(180, 30),
                Size = new Size(100, 35),
                FlatStyle = FlatStyle.System
            };
            _recordButton.Click += (s, e) => StartRecording();
            hotkeyGroup.Controls.Add(_recordButton);

            // Options Section
            _startupCheckBox = new CheckBox
            {
                Text = "Launch at Windows startup",
                Location = new Point(30, 250),
                Size = new Size(250, 25),
                Checked = _settings.LaunchAtStartup
            };
            this.Controls.Add(_startupCheckBox);

            _notifyCheckBox = new CheckBox
            {
                Text = "Show notifications in tray",
                Location = new Point(30, 280),
                Size = new Size(250, 25),
                Checked = _settings.ShowNotifications
            };
            this.Controls.Add(_notifyCheckBox);

            // Actions
            _saveButton = new Button
            {
                Text = "Save",
                Location = new Point(130, 320),
                Size = new Size(90, 35),
                DialogResult = DialogResult.OK
            };
            _saveButton.Click += (s, e) => SaveAndClose();
            this.Controls.Add(_saveButton);

            _cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(230, 320),
                Size = new Size(90, 35),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(_cancelButton);

            this.KeyPreview = true;
            this.KeyDown += OnFormKeyDown;
        }

        private void StartRecording()
        {
            _isRecording = true;
            _recordButton.Enabled = false;
            _recordButton.Text = "...";
            _hotkeyLabel.Text = "Press any key...";
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (_isRecording)
            {
                // Mapping WinForms Keys to SharpHook KeyCode is complex, 
                // but for single keys we can try direct mapping or just use SharpHook in background.
                // For simplicity, let's use a basic mapping or ask user to use common keys.
                // We'll use the raw ScanCode or Mapping for common keys.
                
                // For now, let's map F1-F12 and common keys
                KeyCode? detected = MapWinFormsKeyToSharpHook(e.KeyCode);
                if (detected.HasValue)
                {
                    _tempKeyCode = detected.Value;
                    _hotkeyLabel.Text = $"Current: {_tempKeyCode.ToString().Replace("Vc", "")}";
                    StopRecording();
                }
            }
        }

        private void StopRecording()
        {
            _isRecording = false;
            _recordButton.Enabled = true;
            _recordButton.Text = "Change";
        }

        private void SaveAndClose()
        {
            UpdatedSettings = new AppSettings
            {
                TriggerKey = _tempKeyCode,
                LaunchAtStartup = _startupCheckBox.Checked,
                ShowNotifications = _notifyCheckBox.Checked
            };
            this.Close();
        }

        private KeyCode? MapWinFormsKeyToSharpHook(Keys key)
        {
            // Simple mapping for common hotkeys
            if (key >= Keys.F1 && key <= Keys.F24)
            {
                return (KeyCode)((int)KeyCode.VcF1 + (key - Keys.F1));
            }

            return key switch
            {
                Keys.Scroll => KeyCode.VcScrollLock,
                Keys.Pause => KeyCode.VcPause,
                Keys.Insert => KeyCode.VcInsert,
                Keys.Home => KeyCode.VcHome,
                Keys.PageUp => KeyCode.VcPageUp,
                Keys.PageDown => KeyCode.VcPageDown,
                Keys.End => KeyCode.VcEnd,
                Keys.Capital => KeyCode.VcCapsLock,
                _ => null
            };
        }
    }
}
