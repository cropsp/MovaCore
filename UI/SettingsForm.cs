using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;

namespace MovaCore.UI
{
    public class SettingsForm : Form
    {
        // Sizes are in pixels at 96 DPI: AutoScaleMode.Dpi scales them to the monitor's DPI
        private const int ContentWidth = 430; // inner width of a group; longer texts wrap instead of widening the form
        private const int ComboWidth = 220;
        private const int ButtonMinWidth = 96;
        private const int ButtonMinHeight = 30;
        private const int ExcludedBoxHeight = 72; // about four lines

        // The combo box items, in the order they are listed
        private static readonly CopyPasteKeys[] CopyPasteOptions = { CopyPasteKeys.CtrlCV, CopyPasteKeys.CtrlInsertShiftInsert };
        private static readonly UiLanguage[] LanguageOptions = { UiLanguage.Auto, UiLanguage.English, UiLanguage.Ukrainian };

        private readonly AppSettings _settings;
        private readonly Func<CancellationToken, Task<Hotkey?>> _captureHotkey;
        private readonly CancellationTokenSource _captureCts = new();
        private readonly ToolTip _toolTip = new();
        private readonly Font _titleFont = new("Segoe UI", 11F, FontStyle.Bold);
        private Hotkey _pendingHotkey;

        private readonly PictureBox _logoBox = new();
        private readonly Label _hotkeyLabel = new();
        private readonly Button _changeHotkeyButton = new();
        private readonly CheckBox _restoreClipboardCheckBox = new();
        private readonly CheckBox _switchLayoutCheckBox = new();
        private readonly CheckBox _selectConvertedCheckBox = new();
        private readonly CheckBox _convertLastWordCheckBox = new();
        private readonly ComboBox _copyPasteComboBox = new();
        private readonly CheckBox _startupCheckBox = new();
        private readonly CheckBox _notifyCheckBox = new();
        private readonly ComboBox _languageComboBox = new();
        private readonly TextBox _excludedTextBox = new();
        private readonly Button _saveButton = new();
        private readonly Button _cancelButton = new();

        /// <summary>The settings chosen by the user; null unless the form was closed with Save.</summary>
        public AppSettings? UpdatedSettings { get; private set; }

        /// <param name="currentSettings">The settings to show.</param>
        /// <param name="captureHotkey">
        /// Records the next key combination the user presses (null if cancelled). The hotkey service does it, because the
        /// global hook would otherwise swallow the current trigger while the user is trying to change it.
        /// </param>
        public SettingsForm(AppSettings currentSettings, Func<CancellationToken, Task<Hotkey?>> captureHotkey)
        {
            _settings = currentSettings;
            _captureHotkey = captureHotkey;
            _pendingHotkey = currentSettings.Trigger;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // The panels below size themselves from their content, so longer (translated) texts and larger
            // fonts never get clipped, and the form follows
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = Strings.SettingsTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            AcceptButton = _saveButton;
            CancelButton = _cancelButton;

            _toolTip.AutoPopDelay = 10000;

            TableLayoutPanel root = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            AddRow(root, CreateHeader());
            AddRow(root, CreateHotkeyGroup());
            AddRow(root, CreateConversionGroup());
            AddRow(root, CreateGeneralGroup());
            AddRow(root, CreateExcludedGroup());
            AddRow(root, CreateButtons());
            Controls.Add(root);

            ResumeLayout(false);
            // Size the form now, so that StartPosition.CenterScreen centers its final size
            PerformLayout();
        }

        private Control CreateHeader()
        {
            _logoBox.Size = new Size(64, 64);
            _logoBox.SizeMode = PictureBoxSizeMode.Zoom;
            _logoBox.Margin = new Padding(0, 0, 12, 0);
            try
            {
                _logoBox.Image = AppResources.LoadLogo();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not load the settings logo", ex);
            }

            var title = new Label
            {
                Text = "MovaCore " + Strings.FormatVersion(typeof(SettingsForm).Assembly.GetName().Version),
                Font = _titleFont,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = Padding.Empty,
            };

            TableLayoutPanel header = CreatePanel(new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.AutoSize));
            header.Dock = DockStyle.None;
            header.Anchor = AnchorStyles.None; // centered
            header.Margin = new Padding(0, 0, 0, 10);
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowCount = 1;
            header.Controls.Add(_logoBox, 0, 0);
            header.Controls.Add(title, 1, 0);
            return header;
        }

        private Control CreateHotkeyGroup()
        {
            _hotkeyLabel.Text = _pendingHotkey.ToString();
            _hotkeyLabel.AutoSize = true;
            // The recording prompt is longer than a hotkey: wrap it instead of pushing the button out
            _hotkeyLabel.MaximumSize = new Size(ContentWidth - ButtonMinWidth - 24, 0);
            _hotkeyLabel.Anchor = AnchorStyles.Left;

            _changeHotkeyButton.Text = Strings.HotkeyChange;
            ConfigureButton(_changeHotkeyButton);
            _changeHotkeyButton.Anchor = AnchorStyles.Right;
            _changeHotkeyButton.Click += OnChangeHotkeyClick;

            TableLayoutPanel row = CreatePanel(new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.RowCount = 1;
            row.Controls.Add(_hotkeyLabel, 0, 0);
            row.Controls.Add(_changeHotkeyButton, 1, 0);
            return CreateGroup(Strings.HotkeyGroup, row);
        }

        private Control CreateConversionGroup()
        {
            ConfigureCheckBox(_restoreClipboardCheckBox, Strings.RestoreClipboard, _settings.RestoreClipboard);
            ConfigureCheckBox(_switchLayoutCheckBox, Strings.SwitchLayout, _settings.SwitchLayout);
            ConfigureCheckBox(_selectConvertedCheckBox, Strings.SelectConvertedText, _settings.SelectConvertedText);
            ConfigureCheckBox(_convertLastWordCheckBox, Strings.ConvertLastWord, _settings.ConvertLastWord);
            _toolTip.SetToolTip(_convertLastWordCheckBox, Strings.ConvertLastWordTooltip);

            var copyPasteLabel = new Label { Text = Strings.CopyPasteKeysLabel };
            ConfigureComboBox(
                _copyPasteComboBox,
                Strings.CopyPasteKeysLabel,
                new object[] { Strings.CopyPasteCtrlCV, Strings.CopyPasteCtrlInsert },
                Array.IndexOf(CopyPasteOptions, _settings.CopyPasteKeys));
            _toolTip.SetToolTip(copyPasteLabel, Strings.CopyPasteKeysTooltip);
            _toolTip.SetToolTip(_copyPasteComboBox, Strings.CopyPasteKeysTooltip);

            return CreateGroup(
                Strings.ConversionGroup,
                _restoreClipboardCheckBox,
                _switchLayoutCheckBox,
                _selectConvertedCheckBox,
                _convertLastWordCheckBox,
                CreateLabeledRow(copyPasteLabel, _copyPasteComboBox));
        }

        private Control CreateGeneralGroup()
        {
            ConfigureCheckBox(_startupCheckBox, Strings.LaunchAtStartup, _settings.LaunchAtStartup);
            ConfigureCheckBox(_notifyCheckBox, Strings.ShowNotifications, _settings.ShowNotifications);

            ConfigureComboBox(
                _languageComboBox,
                Strings.LanguageLabel,
                new object[] { Strings.LanguageAuto, Strings.LanguageEnglish, Strings.LanguageUkrainian },
                Array.IndexOf(LanguageOptions, _settings.Language));

            return CreateGroup(
                Strings.GeneralGroup,
                _startupCheckBox,
                _notifyCheckBox,
                CreateLabeledRow(new Label { Text = Strings.LanguageLabel }, _languageComboBox));
        }

        private Control CreateExcludedGroup()
        {
            _excludedTextBox.Multiline = true;
            _excludedTextBox.AcceptsReturn = true; // Enter adds a line instead of pressing Save
            _excludedTextBox.ScrollBars = ScrollBars.Vertical;
            _excludedTextBox.AutoSize = false;
            _excludedTextBox.Size = new Size(ContentWidth, ExcludedBoxHeight);
            _excludedTextBox.MinimumSize = _excludedTextBox.Size; // what the auto-sized layout asks for
            _excludedTextBox.Dock = DockStyle.Fill;
            _excludedTextBox.AccessibleName = Strings.ExcludedGroup;
            _excludedTextBox.Text = string.Join(Environment.NewLine, _settings.ExcludedProcesses);

            var hint = new Label
            {
                Text = Strings.ExcludedHint,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                ForeColor = SystemColors.GrayText,
            };

            return CreateGroup(Strings.ExcludedGroup, _excludedTextBox, hint);
        }

        private Control CreateButtons()
        {
            _saveButton.Text = Strings.Save;
            _saveButton.DialogResult = DialogResult.OK;
            _saveButton.Margin = new Padding(0, 0, 8, 0);
            ConfigureButton(_saveButton);
            _saveButton.Click += OnSaveClick;

            _cancelButton.Text = Strings.Cancel;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Margin = Padding.Empty;
            ConfigureButton(_cancelButton);

            // The first column takes the free space, which pushes both buttons to the right
            TableLayoutPanel row = CreatePanel(
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.AutoSize),
                new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.RowCount = 1;
            row.Controls.Add(_saveButton, 1, 0);
            row.Controls.Add(_cancelButton, 2, 0);
            return row;
        }

        // Layout helpers

        private static TableLayoutPanel CreatePanel(params ColumnStyle[] columns)
        {
            var panel = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = columns.Length,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
            };
            foreach (ColumnStyle column in columns) panel.ColumnStyles.Add(column);
            return panel;
        }

        private static void AddRow(TableLayoutPanel panel, Control control)
        {
            int row = panel.RowStyles.Count;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowCount = row + 1;
            panel.Controls.Add(control, 0, row);
        }

        private static GroupBox CreateGroup(string title, params Control[] rows)
        {
            TableLayoutPanel content = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            foreach (Control row in rows) AddRow(content, row);

            var group = new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 4, 8, 8),
                Margin = new Padding(0, 0, 0, 8),
            };
            group.Controls.Add(content);
            return group;
        }

        private static TableLayoutPanel CreateLabeledRow(Label label, ComboBox comboBox)
        {
            label.AutoSize = true;
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(3, 0, 8, 0);

            TableLayoutPanel row = CreatePanel(new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.RowCount = 1;
            row.Controls.Add(label, 0, 0);
            row.Controls.Add(comboBox, 1, 0);
            return row;
        }

        private static void ConfigureCheckBox(CheckBox checkBox, string text, bool isChecked)
        {
            checkBox.Text = text;
            checkBox.Checked = isChecked;
            checkBox.AutoSize = true;
            checkBox.MaximumSize = new Size(ContentWidth, 0); // wraps a long text instead of widening the form
            checkBox.Anchor = AnchorStyles.Left;
        }

        private static void ConfigureComboBox(ComboBox comboBox, string accessibleName, object[] items, int selectedIndex)
        {
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Width = ComboWidth;
            comboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            comboBox.AccessibleName = accessibleName;
            comboBox.Items.AddRange(items);
            // A value written by hand into settings.json can be unknown: show the first item then
            comboBox.SelectedIndex = Math.Max(selectedIndex, 0);
        }

        private static void ConfigureButton(Button button)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(ButtonMinWidth, ButtonMinHeight);
        }

        // Behavior

        private async void OnChangeHotkeyClick(object? sender, EventArgs e)
        {
            _changeHotkeyButton.Enabled = false;
            _saveButton.Enabled = false;
            _hotkeyLabel.Text = Strings.HotkeyPrompt;

            Hotkey? captured = null;
            try
            {
                captured = await _captureHotkey(_captureCts.Token);
            }
            catch (OperationCanceledException)
            {
                // The form was closed while recording
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not record the hotkey", ex);
            }

            // The form may be gone by now: closing it cancels the recording
            if (IsDisposed) return;

            if (captured is { } hotkey) _pendingHotkey = hotkey;
            _hotkeyLabel.Text = _pendingHotkey.ToString();
            _changeHotkeyButton.Enabled = true;
            _saveButton.Enabled = true;
            _changeHotkeyButton.Focus();
        }

        // The Save button has DialogResult.OK, which closes the dialog after this handler has run
        private void OnSaveClick(object? sender, EventArgs e)
        {
            UpdatedSettings = new AppSettings
            {
                Trigger = _pendingHotkey,
                LaunchAtStartup = _startupCheckBox.Checked,
                ShowNotifications = _notifyCheckBox.Checked,
                RestoreClipboard = _restoreClipboardCheckBox.Checked,
                SwitchLayout = _switchLayoutCheckBox.Checked,
                SelectConvertedText = _selectConvertedCheckBox.Checked,
                ConvertLastWord = _convertLastWordCheckBox.Checked,
                CopyPasteKeys = CopyPasteOptions[Math.Max(_copyPasteComboBox.SelectedIndex, 0)],
                ExcludedProcesses = ParseExcludedProcesses(),
                Language = LanguageOptions[Math.Max(_languageComboBox.SelectedIndex, 0)],
            };
        }

        /// <summary>One process name per line: trimmed, without empty lines and without duplicates (any case).</summary>
        private List<string> ParseExcludedProcesses()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in _excludedTextBox.Lines)
            {
                string name = line.Trim();
                if (name.Length > 0 && seen.Add(name)) names.Add(name);
            }
            return names;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // A recording that is still running ends without a result
            _captureCts.Cancel();
            _captureCts.Dispose();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _captureCts.Dispose();
                _toolTip.Dispose();
                _titleFont.Dispose();
                // PictureBox does not dispose its image
                _logoBox.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
