using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;
using static MovaCore.UI.FormLayout;

namespace MovaCore.UI
{
    /// <summary>
    /// What a new user needs, in one window: where MovaCore lives, the hotkey tried out on a word, voice input turned on
    /// with a dictation key of one's own (many compact keyboards have no ScrollLock) and its model downloading, how to
    /// keep the tray icon in sight, and autostart. Shown at the first run and from the tray menu; closing it in any way
    /// keeps what was chosen in it.
    /// </summary>
    public sealed class FirstStepsForm : Form
    {
        // A word typed with the English layout for "привіт", and what the hotkey makes of it
        private const string SampleText = "ghbdsn";
        private const string ConvertedSample = "привіт";
        private const string TaskbarSettingsUri = "ms-settings:taskbar";

        private readonly AppSettings _settings;
        private readonly ModelDownloadManager _downloads;
        private readonly SpeechModelInfo? _model; // null for a custom model file
        private readonly CancellationTokenSource _captureCts = new();
        private readonly Font _headingFont = new("Segoe UI", 11F, FontStyle.Bold);
        private readonly Font _tryFont = new("Segoe UI", 11F);

        private readonly PictureBox _logoBox = new();
        private readonly TextBox _tryBox = new();
        private readonly Label _tryResult = new();
        private readonly Button _enableVoiceButton = new();
        private readonly Label _voiceStatus = new();
        private readonly ProgressBar _voiceProgress = new();
        private readonly CheckBox _startupCheckBox = new();
        private readonly Button _doneButton = new();
        private HotkeyPicker _speechPicker = null!;
        private bool _voiceEnabled;

        /// <summary>The settings with what was chosen here; set when the window closes.</summary>
        public AppSettings? UpdatedSettings { get; private set; }

        /// <param name="currentSettings">The settings now in use; the window changes a copy.</param>
        /// <param name="captureHotkey">Records the next key combination (see <see cref="SettingsForm"/>).</param>
        /// <param name="downloads">Downloads the speech model when voice input is turned on here.</param>
        /// <param name="firstRun">Suggests autostart, which a new user most likely wants for a tray utility.</param>
        public FirstStepsForm(
            AppSettings currentSettings,
            Func<CancellationToken, Task<Hotkey?>> captureHotkey,
            ModelDownloadManager downloads,
            bool firstRun)
        {
            _settings = currentSettings;
            _downloads = downloads;
            _model = SpeechModelCatalog.Selected(currentSettings.SpeechModel);
            _voiceEnabled = currentSettings.SpeechEnabled;
            InitializeComponent(captureHotkey, firstRun);
            _downloads.StateChanged += OnDownloadStateChanged;
        }

        private void InitializeComponent(Func<CancellationToken, Task<Hotkey?>> captureHotkey, bool firstRun)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = Strings.FirstStepsTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true; // at the first run nothing else shows that MovaCore has started
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            AcceptButton = _doneButton;
            CancelButton = _doneButton;

            string trigger = _settings.Trigger.ToString();
            _speechPicker = new HotkeyPicker(
                _settings.SpeechHotkey,
                ContentWidth - ButtonMinWidth - 24,
                captureHotkey,
                hotkey => hotkey == _settings.Trigger ? Strings.HotkeyUsedForConversion : null,
                _captureCts.Token);
            ConfigureButton(_speechPicker.Button);
            _speechPicker.CaptureStateChanged += (_, recording) => _doneButton.Enabled = !recording;
            _speechPicker.ValueChanged += (_, _) => UpdateVoiceStatus();

            TableLayoutPanel root = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            AddRow(root, CreateHeader());
            AddRow(root, CreateConversionGroup(trigger));
            AddRow(root, CreateVoiceGroup());
            AddRow(root, CreateTrayGroup());

            ConfigureCheckBox(_startupCheckBox, Strings.LaunchAtStartup, _settings.LaunchAtStartup || firstRun);
            _startupCheckBox.Margin = new Padding(3, 0, 3, 8);
            AddRow(root, _startupCheckBox);
            AddRow(root, CreateFooter());
            Controls.Add(root);

            UpdateVoiceStatus();
            ResumeLayout(false);
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
                AppLog.Error("Could not load the logo", ex);
            }

            var heading = new Label { Text = Strings.FirstStepsHeading, Font = _headingFont, AutoSize = true, Margin = Padding.Empty };
            Label intro = CreateText(Strings.FirstStepsIntro);
            intro.MaximumSize = new Size(ContentWidth - 76, 0); // beside the logo
            intro.Margin = new Padding(0, 4, 0, 0);
            TableLayoutPanel texts = CreatePanel(new ColumnStyle(SizeType.AutoSize));
            texts.Dock = DockStyle.None;
            texts.Anchor = AnchorStyles.Left;
            AddRow(texts, heading);
            AddRow(texts, intro);

            TableLayoutPanel header = CreatePanel(new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.AutoSize));
            header.Margin = new Padding(0, 0, 0, 10);
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowCount = 1;
            header.Controls.Add(_logoBox, 0, 0);
            header.Controls.Add(texts, 1, 0);
            return header;
        }

        private Control CreateConversionGroup(string trigger)
        {
            _tryBox.Text = SampleText;
            _tryBox.Width = ContentWidth;
            _tryBox.Font = _tryFont;
            _tryBox.AccessibleName = Strings.FirstStepsConversionGroup;
            _tryBox.Margin = new Padding(3, 6, 3, 3);
            _tryBox.TextChanged += (_, _) =>
                _tryResult.Visible = string.Equals(_tryBox.Text.Trim(), ConvertedSample, StringComparison.OrdinalIgnoreCase);

            _tryResult.Text = Strings.FirstStepsConversionDone(trigger);
            _tryResult.AutoSize = true;
            _tryResult.MaximumSize = new Size(ContentWidth, 0);
            _tryResult.ForeColor = Color.ForestGreen;
            _tryResult.Visible = false;

            return CreateGroup(Strings.FirstStepsConversionGroup, CreateText(Strings.FirstStepsConversionText(trigger)), _tryBox, _tryResult);
        }

        private Control CreateVoiceGroup()
        {
            long size = _model?.ApproximateBytes ?? SpeechModelCatalog.Selected(SpeechModelCatalog.DefaultId)!.ApproximateBytes;

            _enableVoiceButton.Text = Strings.FirstStepsVoiceEnable;
            _enableVoiceButton.Anchor = AnchorStyles.Right;
            ConfigureButton(_enableVoiceButton);
            _enableVoiceButton.Click += OnEnableVoiceClick;

            _voiceStatus.AutoSize = true;
            _voiceStatus.MaximumSize = new Size(ContentWidth - ButtonMinWidth - 24, 0);
            _voiceStatus.Anchor = AnchorStyles.Left;
            TableLayoutPanel statusRow = CreatePanel(new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
            statusRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusRow.RowCount = 1;
            statusRow.Controls.Add(_voiceStatus, 0, 0);
            statusRow.Controls.Add(_enableVoiceButton, 1, 0);

            _voiceProgress.Width = ContentWidth;
            _voiceProgress.Height = 8;
            _voiceProgress.Dock = DockStyle.Fill;
            _voiceProgress.Margin = new Padding(3, 0, 3, 3);

            return CreateGroup(
                Strings.FirstStepsVoiceGroup,
                CreateText(Strings.FirstStepsVoiceText(Strings.FormatSize(size))),
                statusRow,
                _speechPicker,
                _voiceProgress);
        }

        private Control CreateTrayGroup()
        {
            var settingsLink = new LinkLabel { Text = Strings.FirstStepsTraySettings, AutoSize = true, Margin = new Padding(3, 4, 3, 0) };
            settingsLink.LinkClicked += (_, _) => Open(TaskbarSettingsUri);
            return CreateGroup(Strings.FirstStepsTrayGroup, CreateText(Strings.FirstStepsTrayText), settingsLink);
        }

        private Control CreateFooter()
        {
            _doneButton.Text = Strings.FirstStepsDone;
            _doneButton.Anchor = AnchorStyles.Right;
            _doneButton.Margin = Padding.Empty;
            ConfigureButton(_doneButton);
            _doneButton.Click += (_, _) => Close();

            Label reopen = CreateHint(Strings.FirstStepsReopen);
            reopen.MaximumSize = new Size(ContentWidth - ButtonMinWidth - 12, 0);
            reopen.Anchor = AnchorStyles.Left;

            TableLayoutPanel row = CreatePanel(new ColumnStyle(SizeType.Percent, 100), new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.RowCount = 1;
            row.Controls.Add(reopen, 0, 0);
            row.Controls.Add(_doneButton, 1, 0);
            return row;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Ready for the hotkey: the user only has to press it
            _tryBox.Focus();
            _tryBox.SelectAll();
        }

        private void OnEnableVoiceClick(object? sender, EventArgs e)
        {
            _voiceEnabled = true;
            if (_model != null) _downloads.Start(_model); // nothing if it is on disk or downloading already
            UpdateVoiceStatus();
            // ScrollLock is the default, but many compact keyboards have none: ask for the key right away
            _speechPicker.StartCapture(Strings.SpeechHotkeyPrompt(_speechPicker.Value.ToString()));
        }

        private void UpdateVoiceStatus()
        {
            // The key stays in sight (ScrollLock by default), so the window does not grow when voice input is turned on
            _enableVoiceButton.Visible = !_voiceEnabled;
            _speechPicker.Enabled = _voiceEnabled;

            string hotkey = _speechPicker.Value.ToString();
            ModelDownloadState state = _downloads.State;
            bool downloading = _voiceEnabled && _model != null && state.ModelId == _model.Id
                && state.Status == ModelDownloadStatus.Downloading;
            string status;
            if (!_voiceEnabled)
                status = Strings.FirstStepsVoiceOff;
            else if (downloading)
                status = state.TotalBytes is long total
                    ? Strings.SpeechModelDownloading(Strings.FormatSize(state.BytesReceived), Strings.FormatSize(total), state.Percent ?? 0)
                    : Strings.SpeechModelDownloadStarting;
            else if (_model != null && state.ModelId == _model.Id && state.Status == ModelDownloadStatus.Failed && state.Error is { } error)
                status = Strings.SpeechModelDownloadFailed(Strings.DownloadErrorText(error));
            else if (_model == null || _downloads.IsDownloaded(_model))
                status = Strings.FirstStepsVoiceOn(hotkey);
            else
                status = Strings.FirstStepsVoiceWaitsForModel;

            // Progress arrives several times a second: touch only what changed (see SettingsForm.UpdateModelStatus)
            if (_voiceStatus.Text != status) _voiceStatus.Text = status;
            if (_voiceProgress.Visible != downloading) _voiceProgress.Visible = downloading;
            if (downloading) _voiceProgress.Value = Math.Clamp(state.Percent ?? 0, 0, 100);
        }

        // Raised on a worker thread
        private void OnDownloadStateChanged(object? sender, ModelDownloadState state)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(UpdateVoiceStatus);
            }
            catch (InvalidOperationException)
            {
                // The window is closing
            }
        }

        private static void Open(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not open the taskbar settings", ex);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;

            AppSettings updated = _settings.Clone();
            updated.LaunchAtStartup = _startupCheckBox.Checked;
            updated.SpeechEnabled = _voiceEnabled;
            updated.SpeechHotkey = _speechPicker.Value;
            UpdatedSettings = updated;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _downloads.StateChanged -= OnDownloadStateChanged;
            _captureCts.Cancel(); // a recording that is still running ends without a result
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _downloads.StateChanged -= OnDownloadStateChanged;
                _captureCts.Dispose();
                _headingFont.Dispose();
                _tryFont.Dispose();
                _logoBox.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
