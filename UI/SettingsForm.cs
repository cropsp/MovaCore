using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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
        private const string ModelsPageUrl = "https://huggingface.co/ggerganov/whisper.cpp/tree/main";

        // The combo box items, in the order they are listed
        private static readonly CopyPasteKeys[] CopyPasteOptions = { CopyPasteKeys.CtrlCV, CopyPasteKeys.CtrlInsertShiftInsert };
        private static readonly UiLanguage[] LanguageOptions = { UiLanguage.Auto, UiLanguage.English, UiLanguage.Ukrainian };

        private readonly AppSettings _settings;
        private readonly Func<CancellationToken, Task<Hotkey?>> _captureHotkey;
        private readonly ModelDownloadManager _downloads;
        private readonly List<string?> _microphoneIds = new(); // per item of _microphoneComboBox; null = Windows default
        private readonly CancellationTokenSource _captureCts = new();
        private readonly ToolTip _toolTip = new();
        private readonly Font _titleFont = new("Segoe UI", 11F, FontStyle.Bold);
        private string _customModelPath;

        private readonly PictureBox _logoBox = new();
        private readonly TabControl _tabs = new();
        private readonly List<TableLayoutPanel> _pages = new();
        private HotkeyPicker _triggerPicker = null!;
        private HotkeyPicker _speechPicker = null!;
        private readonly CheckBox _restoreClipboardCheckBox = new();
        private readonly CheckBox _switchLayoutCheckBox = new();
        private readonly CheckBox _selectConvertedCheckBox = new();
        private readonly CheckBox _convertLastWordCheckBox = new();
        private readonly ComboBox _copyPasteComboBox = new();
        private readonly CheckBox _startupCheckBox = new();
        private readonly CheckBox _notifyCheckBox = new();
        private readonly ComboBox _languageComboBox = new();
        private readonly TextBox _excludedTextBox = new();
        private readonly CheckBox _speechEnabledCheckBox = new();
        private readonly ComboBox _modelComboBox = new();
        private readonly Label _modelStatusLabel = new();
        private readonly Button _modelActionButton = new();
        private readonly ProgressBar _modelProgress = new();
        private readonly TableLayoutPanel _customModelRow = new();
        private readonly TextBox _customModelTextBox = new();
        private readonly Button _browseButton = new();
        private readonly LinkLabel _whereToGetLink = new();
        private readonly ComboBox _speechLanguageComboBox = new();
        private readonly ComboBox _microphoneComboBox = new();
        private readonly CheckBox _useGpuCheckBox = new();
        private readonly CheckBox _overlayCheckBox = new();
        private readonly Button _saveButton = new();
        private readonly Button _cancelButton = new();

        /// <summary>The settings chosen by the user; null unless the form was closed with Save.</summary>
        public AppSettings? UpdatedSettings { get; private set; }

        /// <param name="currentSettings">The settings to show.</param>
        /// <param name="captureHotkey">
        /// Records the next key combination the user presses (null if cancelled). The hotkey service does it, because the
        /// global hook would otherwise swallow the current trigger while the user is trying to change it.
        /// </param>
        /// <param name="microphones">The microphones to offer.</param>
        /// <param name="downloads">Shows and controls the download of the speech model.</param>
        public SettingsForm(
            AppSettings currentSettings,
            Func<CancellationToken, Task<Hotkey?>> captureHotkey,
            IReadOnlyList<AudioInputDevice> microphones,
            ModelDownloadManager downloads)
        {
            _settings = currentSettings;
            _captureHotkey = captureHotkey;
            _downloads = downloads;
            _customModelPath = currentSettings.SpeechCustomModelPath;
            InitializeComponent(microphones);
            _downloads.StateChanged += OnDownloadStateChanged;
        }

        /// <summary>For the smoke test, which shows every page.</summary>
        internal TabControl Tabs => _tabs;

        private void InitializeComponent(IReadOnlyList<AudioInputDevice> microphones)
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

            AddPage(Strings.TabLayout, CreateHotkeyGroup(), CreateConversionGroup());
            AddPage(Strings.TabVoice, CreateVoicePage(microphones));
            AddPage(Strings.TabGeneral, CreateGeneralGroup(), CreateExcludedGroup());
            _tabs.Margin = new Padding(0, 0, 0, 10);

            TableLayoutPanel root = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            AddRow(root, CreateHeader());
            AddRow(root, _tabs);
            AddRow(root, CreateButtons());
            Controls.Add(root);

            FitTabsToPages(); // also applies UpdateVoiceControls
            ResumeLayout(false);
            // Size the form now, so that StartPosition.CenterScreen centers its final size
            PerformLayout();
        }

        private void AddPage(string title, params Control[] rows)
        {
            TableLayoutPanel content = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            content.Dock = DockStyle.None;
            content.Location = new Point(8, 8);
            foreach (Control row in rows) AddRow(content, row);
            _pages.Add(content);

            var page = new TabPage(title) { BackColor = Color.White, UseVisualStyleBackColor = false };
            page.Controls.Add(content);
            _tabs.TabPages.Add(page);
        }

        // A TabControl does not size itself: make it as large as the largest page, plus its own frame and tab strip.
        // The Voice page is measured with every optional row shown and its longest status text, so that nothing it
        // shows later is cut off.
        private void FitTabsToPages()
        {
            foreach (Control optional in new Control[] { _customModelRow, _whereToGetLink, _modelProgress, _modelActionButton })
                optional.Visible = true;
            _modelStatusLabel.Text = LongestModelStatus();

            var largest = Size.Empty;
            foreach (TableLayoutPanel page in _pages)
            {
                Size preferred = page.GetPreferredSize(Size.Empty);
                largest = new Size(Math.Max(largest.Width, preferred.Width), Math.Max(largest.Height, preferred.Height));
            }
            UpdateVoiceControls(); // back to what the settings show

            // The same margin on the far sides as the content's offset (scaled with the DPI)
            Point offset = _pages[0].Location;
            largest += new Size(2 * offset.X, 2 * offset.Y);

            // DisplayRectangle needs the handle; before it exists, estimate the frame from the font
            Size frame = _tabs.IsHandleCreated
                ? _tabs.Size - _tabs.DisplayRectangle.Size
                : new Size(8, Font.Height + 16);
            _tabs.Size = largest + frame;
        }

        private static string LongestModelStatus()
        {
            var candidates = new List<string>
            {
                Strings.SpeechModelNotDownloaded(Strings.FormatSize(1_624_600_000)),
                Strings.SpeechModelDownloading(Strings.FormatSize(1_624_600_000), Strings.FormatSize(1_624_600_000), 100),
                Strings.SpeechCustomGguf,
                Strings.SpeechCustomNone,
            };
            foreach (ModelDownloadError error in Enum.GetValues<ModelDownloadError>())
                candidates.Add(Strings.SpeechModelDownloadFailed(Strings.DownloadErrorText(error)));

            string longest = "";
            foreach (string candidate in candidates)
            {
                if (candidate.Length > longest.Length) longest = candidate;
            }
            return longest;
        }

        protected override void OnLoad(EventArgs e)
        {
            // Before base.OnLoad, which centers the form on the screen
            FitTabsToPages();
            base.OnLoad(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            FitTabsToPages();
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

        private HotkeyPicker CreateHotkeyPicker(Hotkey value, Func<Hotkey, string?> validate)
        {
            var picker = new HotkeyPicker(value, ContentWidth - ButtonMinWidth - 24, _captureHotkey, validate, _captureCts.Token);
            ConfigureButton(picker.Button);
            picker.CaptureStateChanged += (_, recording) =>
            {
                _saveButton.Enabled = !recording;
                _triggerPicker.Button.Enabled = _speechPicker.Button.Enabled = !recording;
            };
            return picker;
        }

        private Control CreateHotkeyGroup()
        {
            _triggerPicker = CreateHotkeyPicker(
                _settings.Trigger,
                hotkey => hotkey == _speechPicker.Value ? Strings.HotkeyUsedForDictation : null);
            return CreateGroup(Strings.HotkeyGroup, _triggerPicker);
        }

        private Control CreateConversionGroup()
        {
            ConfigureCheckBox(_switchLayoutCheckBox, Strings.SwitchLayout, _settings.SwitchLayout);
            ConfigureCheckBox(_selectConvertedCheckBox, Strings.SelectConvertedText, _settings.SelectConvertedText);
            ConfigureCheckBox(_convertLastWordCheckBox, Strings.ConvertLastWord, _settings.ConvertLastWord);
            _toolTip.SetToolTip(_convertLastWordCheckBox, Strings.ConvertLastWordTooltip);

            return CreateGroup(
                Strings.ConversionGroup,
                _switchLayoutCheckBox,
                _selectConvertedCheckBox,
                _convertLastWordCheckBox);
        }

        private Control CreateVoicePage(IReadOnlyList<AudioInputDevice> microphones)
        {
            ConfigureCheckBox(_speechEnabledCheckBox, Strings.SpeechEnable, _settings.SpeechEnabled);
            _speechEnabledCheckBox.Margin = new Padding(3, 3, 3, 8);
            _speechEnabledCheckBox.CheckedChanged += (_, _) => UpdateVoiceControls();

            _speechPicker = CreateHotkeyPicker(
                _settings.SpeechHotkey,
                hotkey => hotkey == _triggerPicker.Value ? Strings.HotkeyUsedForConversion : null);

            TableLayoutPanel content = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            AddRow(content, _speechEnabledCheckBox);
            AddRow(content, CreateGroup(Strings.SpeechHotkeyGroup, _speechPicker));
            AddRow(content, CreateModelGroup());
            AddRow(content, CreateRecognitionGroup(microphones));
            AddRow(content, CreateHint(Strings.SpeechPrivacy));
            return content;
        }

        private Control CreateModelGroup()
        {
            var items = new List<object>();
            foreach (SpeechModelInfo model in SpeechModelCatalog.Models) items.Add(Strings.SpeechModelName(model));
            items.Add(Strings.SpeechModelCustom);
            SpeechModelInfo? selected = SpeechModelCatalog.Selected(_settings.SpeechModel);
            int selectedIndex = selected == null
                ? items.Count - 1
                : IndexOf(SpeechModelCatalog.Models, selected);
            ConfigureComboBox(_modelComboBox, Strings.SpeechModelGroup, items.ToArray(), selectedIndex);
            _modelComboBox.Width = ContentWidth;
            _modelComboBox.SelectedIndexChanged += (_, _) => UpdateVoiceControls();

            _modelStatusLabel.AutoSize = true;
            _modelStatusLabel.MaximumSize = new Size(ContentWidth - ButtonMinWidth - 24, 0);
            _modelStatusLabel.Anchor = AnchorStyles.Left;
            _modelActionButton.Anchor = AnchorStyles.Right;
            ConfigureButton(_modelActionButton);
            _modelActionButton.Click += OnModelActionClick;

            TableLayoutPanel statusRow = CreatePanel(new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
            statusRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusRow.RowCount = 1;
            statusRow.Controls.Add(_modelStatusLabel, 0, 0);
            statusRow.Controls.Add(_modelActionButton, 1, 0);

            _modelProgress.Width = ContentWidth;
            _modelProgress.Height = 8;
            _modelProgress.Dock = DockStyle.Fill;
            _modelProgress.Margin = new Padding(3, 0, 3, 3);

            _customModelTextBox.ReadOnly = true;
            _customModelTextBox.Width = ContentWidth - ButtonMinWidth - 24;
            _customModelTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _customModelTextBox.AccessibleName = Strings.SpeechModelCustom;
            _customModelTextBox.Text = _customModelPath;
            _browseButton.Text = Strings.SpeechModelBrowse;
            _browseButton.Anchor = AnchorStyles.Right;
            ConfigureButton(_browseButton);
            _browseButton.Click += OnBrowseClick;

            _customModelRow.AutoSize = true;
            _customModelRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _customModelRow.Dock = DockStyle.Fill;
            _customModelRow.Margin = Padding.Empty;
            _customModelRow.ColumnCount = 2;
            _customModelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _customModelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _customModelRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _customModelRow.RowCount = 1;
            _customModelRow.Controls.Add(_customModelTextBox, 0, 0);
            _customModelRow.Controls.Add(_browseButton, 1, 0);

            _whereToGetLink.Text = Strings.SpeechModelWhereToGet;
            _whereToGetLink.AutoSize = true;
            _whereToGetLink.Anchor = AnchorStyles.Left;
            _whereToGetLink.LinkClicked += (_, _) => OpenInBrowser(ModelsPageUrl);

            return CreateGroup(Strings.SpeechModelGroup, _modelComboBox, _customModelRow, statusRow, _modelProgress, _whereToGetLink);
        }

        private Control CreateRecognitionGroup(IReadOnlyList<AudioInputDevice> microphones)
        {
            var languageItems = new List<object>();
            foreach (string code in SpeechLanguages.Codes) languageItems.Add(Strings.SpeechLanguageName(code));
            ConfigureComboBox(
                _speechLanguageComboBox,
                Strings.SpeechLanguageLabel,
                languageItems.ToArray(),
                IndexOf(SpeechLanguages.Codes, _settings.SpeechLanguage));

            var microphoneItems = new List<object> { Strings.SpeechMicrophoneDefault };
            _microphoneIds.Add(null);
            int selectedMicrophone = 0;
            foreach (AudioInputDevice microphone in microphones)
            {
                if (microphone.Id == _settings.SpeechMicrophoneId) selectedMicrophone = microphoneItems.Count;
                microphoneItems.Add(microphone.Name);
                _microphoneIds.Add(microphone.Id);
            }
            // Keep a microphone that is unplugged right now: dictation uses the default one until it is back
            if (_settings.SpeechMicrophoneId != null && selectedMicrophone == 0)
            {
                selectedMicrophone = microphoneItems.Count;
                microphoneItems.Add(Strings.SpeechMicrophoneUnavailable);
                _microphoneIds.Add(_settings.SpeechMicrophoneId);
            }
            ConfigureComboBox(_microphoneComboBox, Strings.SpeechMicrophoneLabel, microphoneItems.ToArray(), selectedMicrophone);

            ConfigureCheckBox(_useGpuCheckBox, Strings.SpeechUseGpu, _settings.SpeechUseGpu);
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                // Whisper.net has no Vulkan build for ARM
                _useGpuCheckBox.Checked = false;
                _useGpuCheckBox.Enabled = false;
                _toolTip.SetToolTip(_useGpuCheckBox, Strings.SpeechUseGpuUnavailable);
            }
            else
            {
                _toolTip.SetToolTip(_useGpuCheckBox, Strings.SpeechUseGpuTooltip);
            }
            ConfigureCheckBox(_overlayCheckBox, Strings.SpeechShowOverlay, _settings.SpeechShowOverlay);

            return CreateGroup(
                Strings.SpeechRecognitionGroup,
                CreateLabeledRow(new Label { Text = Strings.SpeechLanguageLabel }, _speechLanguageComboBox),
                CreateLabeledRow(new Label { Text = Strings.SpeechMicrophoneLabel }, _microphoneComboBox),
                _useGpuCheckBox,
                _overlayCheckBox);
        }

        private Control CreateGeneralGroup()
        {
            ConfigureCheckBox(_startupCheckBox, Strings.LaunchAtStartup, _settings.LaunchAtStartup);
            ConfigureCheckBox(_notifyCheckBox, Strings.ShowNotifications, _settings.ShowNotifications);
            ConfigureCheckBox(_restoreClipboardCheckBox, Strings.RestoreClipboard, _settings.RestoreClipboard);

            ConfigureComboBox(
                _languageComboBox,
                Strings.LanguageLabel,
                new object[] { Strings.LanguageAuto, Strings.LanguageEnglish, Strings.LanguageUkrainian },
                Array.IndexOf(LanguageOptions, _settings.Language));

            var copyPasteLabel = new Label { Text = Strings.CopyPasteKeysLabel };
            ConfigureComboBox(
                _copyPasteComboBox,
                Strings.CopyPasteKeysLabel,
                new object[] { Strings.CopyPasteCtrlCV, Strings.CopyPasteCtrlInsert },
                Array.IndexOf(CopyPasteOptions, _settings.CopyPasteKeys));
            _toolTip.SetToolTip(copyPasteLabel, Strings.CopyPasteKeysTooltip);
            _toolTip.SetToolTip(_copyPasteComboBox, Strings.CopyPasteKeysTooltip);

            return CreateGroup(
                Strings.GeneralGroup,
                _startupCheckBox,
                _notifyCheckBox,
                _restoreClipboardCheckBox,
                CreateLabeledRow(copyPasteLabel, _copyPasteComboBox),
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

            return CreateGroup(Strings.ExcludedGroup, _excludedTextBox, CreateHint(Strings.ExcludedHint));
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

        private static Label CreateHint(string text) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = SystemColors.GrayText,
        };

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

        private static int IndexOf<T>(IReadOnlyList<T> items, T item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(items[i], item)) return i;
            }
            return -1;
        }

        // Behavior

        /// <summary>The catalog model chosen in the combo box, or null for a custom file.</summary>
        private SpeechModelInfo? SelectedModel =>
            _modelComboBox.SelectedIndex >= 0 && _modelComboBox.SelectedIndex < SpeechModelCatalog.Models.Count
                ? SpeechModelCatalog.Models[_modelComboBox.SelectedIndex]
                : null;

        private void UpdateVoiceControls()
        {
            // The download button stays usable: a running download can always be cancelled
            bool enabled = _speechEnabledCheckBox.Checked;
            foreach (Control control in new Control[]
            {
                _speechPicker, _modelComboBox, _customModelRow, _speechLanguageComboBox, _microphoneComboBox, _overlayCheckBox,
            })
            {
                control.Enabled = enabled;
            }
            _useGpuCheckBox.Enabled = enabled && RuntimeInformation.ProcessArchitecture != Architecture.Arm64;

            SpeechModelInfo? model = SelectedModel;
            _customModelRow.Visible = model == null;
            _whereToGetLink.Visible = model == null;
            UpdateModelStatus();
        }

        private void UpdateModelStatus()
        {
            SpeechModelInfo? model = SelectedModel;
            if (model == null)
            {
                _modelStatusLabel.Text = string.IsNullOrWhiteSpace(_customModelPath)
                    ? Strings.SpeechCustomNone
                    : SpeechModelFile.Check(_customModelPath) switch
                    {
                        SpeechModelFormat.Ggml => Strings.SpeechCustomGgml,
                        SpeechModelFormat.Gguf => Strings.SpeechCustomGguf,
                        SpeechModelFormat.Missing => Strings.SpeechCustomMissing,
                        _ => Strings.SpeechCustomUnknown,
                    };
                _modelActionButton.Visible = false;
                _modelProgress.Visible = false;
                return;
            }

            ModelDownloadState state = _downloads.State;
            bool thisModel = state.ModelId == model.Id;
            _modelProgress.Visible = false;
            _modelActionButton.Visible = true;
            _modelActionButton.Text = Strings.SpeechModelDownload;

            if (thisModel && state.Status == ModelDownloadStatus.Downloading)
            {
                _modelStatusLabel.Text = state.TotalBytes is long total
                    ? Strings.SpeechModelDownloading(Strings.FormatSize(state.BytesReceived), Strings.FormatSize(total), state.Percent ?? 0)
                    : Strings.SpeechModelDownloadStarting;
                _modelActionButton.Text = Strings.Cancel;
                _modelProgress.Visible = true;
                _modelProgress.Value = Math.Clamp(state.Percent ?? 0, 0, 100);
            }
            else if (_downloads.IsDownloaded(model))
            {
                _modelStatusLabel.Text = Strings.SpeechModelReady;
                _modelActionButton.Visible = false;
            }
            else if (thisModel && state.Status == ModelDownloadStatus.Failed && state.Error is { } error)
            {
                _modelStatusLabel.Text = Strings.SpeechModelDownloadFailed(Strings.DownloadErrorText(error));
            }
            else
            {
                _modelStatusLabel.Text = Strings.SpeechModelNotDownloaded(Strings.FormatSize(model.ApproximateBytes));
            }
        }

        private void OnModelActionClick(object? sender, EventArgs e)
        {
            if (SelectedModel is not { } model) return;

            ModelDownloadState state = _downloads.State;
            if (state.Status == ModelDownloadStatus.Downloading && state.ModelId == model.Id)
                _downloads.Cancel();
            else
                _downloads.Start(model);
            UpdateModelStatus();
        }

        // Raised on a worker thread
        private void OnDownloadStateChanged(object? sender, ModelDownloadState state)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(UpdateModelStatus);
            }
            catch (InvalidOperationException)
            {
                // The window is closing
            }
        }

        private void OnBrowseClick(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = Strings.SpeechModelFileFilter,
                CheckFileExists = true,
            };
            if (File.Exists(_customModelPath))
                dialog.InitialDirectory = Path.GetDirectoryName(_customModelPath);

            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _customModelPath = dialog.FileName;
            _customModelTextBox.Text = _customModelPath;
            UpdateModelStatus();
        }

        private static void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not open the browser", ex);
            }
        }

        // The Save button has DialogResult.OK, which closes the dialog after this handler has run
        private void OnSaveClick(object? sender, EventArgs e)
        {
            UpdatedSettings = new AppSettings
            {
                Trigger = _triggerPicker.Value,
                LaunchAtStartup = _startupCheckBox.Checked,
                ShowNotifications = _notifyCheckBox.Checked,
                RestoreClipboard = _restoreClipboardCheckBox.Checked,
                SwitchLayout = _switchLayoutCheckBox.Checked,
                SelectConvertedText = _selectConvertedCheckBox.Checked,
                ConvertLastWord = _convertLastWordCheckBox.Checked,
                CopyPasteKeys = CopyPasteOptions[Math.Max(_copyPasteComboBox.SelectedIndex, 0)],
                ExcludedProcesses = ParseExcludedProcesses(),
                Language = LanguageOptions[Math.Max(_languageComboBox.SelectedIndex, 0)],
                SpeechEnabled = _speechEnabledCheckBox.Checked,
                SpeechHotkey = _speechPicker.Value,
                SpeechModel = SelectedModel?.Id ?? SpeechModelCatalog.CustomId,
                SpeechCustomModelPath = _customModelPath,
                SpeechLanguage = SpeechLanguages.Codes[Math.Max(_speechLanguageComboBox.SelectedIndex, 0)],
                SpeechMicrophoneId = _microphoneIds[Math.Max(_microphoneComboBox.SelectedIndex, 0)],
                // Kept as it was on ARM, where the box is always disabled
                SpeechUseGpu = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? _settings.SpeechUseGpu
                    : _useGpuCheckBox.Checked,
                SpeechShowOverlay = _overlayCheckBox.Checked,
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
            _downloads.StateChanged -= OnDownloadStateChanged;
            // A recording that is still running ends without a result
            _captureCts.Cancel();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _downloads.StateChanged -= OnDownloadStateChanged;
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
