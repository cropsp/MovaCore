using System;
using System.Diagnostics;
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
        private const string AppName = "MovaCore";
        private const string ReleasesUrl = "https://github.com/cropsp/MovaCore/releases";

        private readonly NotifyIcon _notifyIcon;
        private readonly Icon? _trayIcon;
        private readonly Icon? _recordingIcon;
        private readonly Icon? _transcribingIcon;
        private readonly IHotkeyService _hotkeyService;
        private readonly HotkeyOrchestrator _orchestrator;
        private readonly SettingsService _settingsService;
        private readonly SpeechOrchestrator _speech;
        private readonly IAudioRecorder _recorder;
        private readonly ModelDownloadManager _downloads;
        private readonly RecordingOverlay _overlay;
        private readonly SynchronizationContext _uiContext;
        private AppSettings _currentSettings;
        private SpeechState _speechState = SpeechState.Idle; // UI thread only

        // Created together with the tray menu, after the language is known (see ApplyLanguage)
        private ToolStripMenuItem? _settingsItem;
        private ToolStripMenuItem? _pauseItem;
        private ToolStripMenuItem? _aboutItem;
        private ToolStripMenuItem? _exitItem;

        private SettingsForm? _settingsForm; // the open settings window, if any
        private bool _paused; // not persisted: MovaCore always starts active

        public TrayApplicationContext(
            IHotkeyService hotkeyService,
            HotkeyOrchestrator orchestrator,
            SettingsService settingsService,
            SpeechOrchestrator speech,
            IAudioRecorder recorder,
            ModelDownloadManager downloads)
        {
            _hotkeyService = hotkeyService;
            _orchestrator = orchestrator;
            _settingsService = settingsService;
            _speech = speech;
            _recorder = recorder;
            _downloads = downloads;

            // Load and apply settings
            _currentSettings = _settingsService.Load();
            ApplyLanguage(); // before the tray icon and its menu are created, so they start in the right language

            // Initialize NotifyIcon
            _trayIcon = LoadTrayIcon(AppResources.LoadIcon);
            _recordingIcon = LoadTrayIcon(AppResources.LoadRecordingIcon);
            _transcribingIcon = LoadTrayIcon(AppResources.LoadTranscribingIcon);
            _overlay = new RecordingOverlay(buffer => _speech.CopyRecentAudio(buffer));
            _notifyIcon = new NotifyIcon
            {
                Icon = _trayIcon ?? SystemIcons.Application,
                Text = Strings.TrayTooltip,
                ContextMenuStrip = CreateContextMenu(),
                Visible = true
            };
            _notifyIcon.MouseDoubleClick += OnTrayDoubleClick;

            // The events below arrive on worker threads, but NotifyIcon may only be used on this (UI) thread
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _orchestrator.ConversionFailed += OnConversionFailed;
            _hotkeyService.HookFailed += OnHookFailed;
            _speech.StateChanged += OnSpeechStateChanged;
            _downloads.StateChanged += OnDownloadStateChanged;

            // Applied once the handlers are in place: it may start (resume) the model download
            ApplySettings();

            // Start Hotkey Service
            _hotkeyService.HotkeyTriggered += OnHotkeyTriggered;
            _hotkeyService.SpeechHotkeyPressed += OnSpeechHotkeyPressed;
            _hotkeyService.SpeechHotkeyReleased += OnSpeechHotkeyReleased;
            _hotkeyService.Start();
        }

        private void ApplySettings()
        {
            _hotkeyService.SetTrigger(_currentSettings.Trigger);
            _hotkeyService.SetExcludedProcesses(_currentSettings.ExcludedProcesses);
            _hotkeyService.CopyPasteKeys = _currentSettings.CopyPasteKeys;
            _orchestrator.RestoreClipboard = _currentSettings.RestoreClipboard;
            _orchestrator.SwitchLayout = _currentSettings.SwitchLayout;
            _orchestrator.SelectConvertedText = _currentSettings.SelectConvertedText;
            _orchestrator.ConvertLastWord = _currentSettings.ConvertLastWord;
            ApplySpeechSettings();
        }

        private void ApplySpeechSettings()
        {
            AppSettings s = _currentSettings;
            _hotkeyService.SetSpeechHotkey(s.SpeechEnabled ? s.SpeechHotkey : null);
            _speech.Configure(new SpeechSettings(
                s.SpeechEnabled,
                SpeechModelCatalog.ResolvePath(s.SpeechModel, s.SpeechCustomModelPath, _downloads.ModelsDirectory),
                s.SpeechLanguage,
                s.SpeechUseGpu,
                s.SpeechMicrophoneId,
                s.RestoreClipboard));

            // Turning dictation on downloads the chosen model (and resumes an interrupted download at startup), unless
            // the user has just cancelled that download in the settings
            ModelDownloadState download = _downloads.State;
            if (s.SpeechEnabled && SpeechModelCatalog.Selected(s.SpeechModel) is { } model
                && !(download.Status == ModelDownloadStatus.Cancelled && download.ModelId == model.Id))
            {
                _downloads.Start(model);
            }
        }

        private void ApplyLanguage()
        {
            Strings.Language = WindowsLanguage.Resolve(_currentSettings.Language);
            UpdateTrayTexts();
        }

        // Does nothing while the tray icon does not exist yet: the constructor applies the language first
        private void UpdateTrayTexts()
        {
            if (_settingsItem == null || _pauseItem == null || _aboutItem == null || _exitItem == null) return;

            _settingsItem.Text = Strings.MenuSettings;
            _pauseItem.Text = Strings.MenuPause;
            _pauseItem.Checked = _paused;
            _aboutItem.Text = Strings.MenuAbout;
            _exitItem.Text = Strings.MenuExit;
            UpdateTrayStatus();
        }

        // The icon and its tooltip show what MovaCore is doing right now
        private void UpdateTrayStatus()
        {
            ModelDownloadState download = _downloads.State;
            _notifyIcon.Text = _paused ? Strings.TrayTooltipPaused
                : _speechState == SpeechState.Recording ? Strings.TrayTooltipRecording
                : _speechState == SpeechState.Transcribing ? Strings.TrayTooltipTranscribing
                : download.Status == ModelDownloadStatus.Downloading ? Strings.TrayTooltipDownloading(download.Percent)
                : Strings.TrayTooltip;

            Icon? icon = _speechState switch
            {
                SpeechState.Recording => _recordingIcon,
                SpeechState.Transcribing => _transcribingIcon,
                _ => null,
            };
            _notifyIcon.Icon = icon ?? _trayIcon ?? SystemIcons.Application;
        }

        private static Icon? LoadTrayIcon(Func<Size, Icon?> load)
        {
            try
            {
                return load(SystemInformation.SmallIconSize);
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
                    _notifyIcon.ShowBalloonTip(3000, AppName, message, ToolTipIcon.Info);
                }
            }, null);
        }

        // Raised on a worker thread
        private void OnSpeechStateChanged(object? sender, SpeechStateChangedEventArgs e)
        {
            _uiContext.Post(_ => ShowSpeechState(e), null);
        }

        private void ShowSpeechState(SpeechStateChangedEventArgs e)
        {
            _speechState = e.State;
            UpdateTrayStatus();

            bool overlay = _currentSettings.SpeechShowOverlay;
            switch (e.State)
            {
                case SpeechState.Recording:
                    if (overlay) _overlay.ShowRecording();
                    return;
                case SpeechState.Transcribing:
                    if (overlay) _overlay.ShowTranscribing();
                    return;
            }

            // Not pasted (no text field had the focus): the text stays on the clipboard without a word, the user asked
            // for fewer messages
            string? message = e.Outcome switch
            {
                SpeechOutcome.Failed when e.Error == SpeechError.ModelMissing
                    && _downloads.State.Status == ModelDownloadStatus.Downloading
                    => Strings.SpeechModelStillDownloading(_downloads.State.Percent),
                SpeechOutcome.Failed => Strings.SpeechErrorText(e.Error ?? SpeechError.Failed, e.Detail),
                SpeechOutcome.NoSpeech => Strings.OverlayNoSpeech,
                SpeechOutcome.NoSignal => Strings.OverlayNoSignal,
                _ => null,
            };
            if (message == null)
            {
                _overlay.HideOverlay();
            }
            else if (overlay)
            {
                _overlay.ShowMessage(message);
            }
            else
            {
                // The user just pressed the hotkey and nothing happened: say why, even with notifications off
                _notifyIcon.ShowBalloonTip(4000, AppName, message, ToolTipIcon.Warning);
            }
        }

        // Raised on a worker thread, several times a second while downloading
        private void OnDownloadStateChanged(object? sender, ModelDownloadState state)
        {
            _uiContext.Post(_ => ShowDownloadState(state), null);
        }

        private void ShowDownloadState(ModelDownloadState state)
        {
            UpdateTrayStatus();

            // A downloaded model is loaded right away, so the first dictation does not wait for it
            if (state.Status == ModelDownloadStatus.Completed && state.ModelId == _currentSettings.SpeechModel)
                ApplySpeechSettings();
            if (!_currentSettings.ShowNotifications) return;

            if (state.Status == ModelDownloadStatus.Completed && state.ModelId == _currentSettings.SpeechModel)
            {
                _notifyIcon.ShowBalloonTip(
                    4000, AppName, Strings.BalloonModelReady(_currentSettings.SpeechHotkey.ToString()), ToolTipIcon.Info);
            }
            else if (state.Status == ModelDownloadStatus.Failed && state.Error is { } error)
            {
                _notifyIcon.ShowBalloonTip(
                    5000, AppName, Strings.BalloonModelDownloadFailed(Strings.DownloadErrorText(error)), ToolTipIcon.Warning);
            }
        }

        private void OnHookFailed(object? sender, Exception error)
        {
            // Shown even with notifications off: without the hook the hotkey silently does nothing
            _uiContext.Post(_ => _notifyIcon.ShowBalloonTip(
                5000,
                AppName,
                Strings.BalloonHookFailed(error.Message),
                ToolTipIcon.Error), null);
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            _settingsItem = new ToolStripMenuItem(Strings.MenuSettings, null, (s, e) => ShowSettings());
            _pauseItem = new ToolStripMenuItem(Strings.MenuPause, null, (s, e) => TogglePause());
            _aboutItem = new ToolStripMenuItem(Strings.MenuAbout, null, (s, e) => ShowAbout());
            _exitItem = new ToolStripMenuItem(Strings.MenuExit, null, (s, e) => Exit());

            menu.Items.Add(_settingsItem);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_aboutItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_exitItem);

            return menu;
        }

        private void OnTrayDoubleClick(object? sender, MouseEventArgs e)
        {
            // NotifyIcon reports a double click of the right button too
            if (e.Button == MouseButtons.Left) ShowSettings();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
            {
                _hotkeyService.Stop();
                _speech.Cancel(); // the release of a held dictation hotkey would never arrive
            }
            else
            {
                _hotkeyService.Start();
            }

            UpdateTrayTexts();
        }

        private void OnHotkeyTriggered(object? sender, EventArgs e)
        {
            Task.Run(async () => await _orchestrator.ExecuteConversionAsync());
        }

        // Raised on the hook thread: the orchestrator only queues them
        private void OnSpeechHotkeyPressed(object? sender, EventArgs e) => _speech.OnHotkeyPressed();

        private void OnSpeechHotkeyReleased(object? sender, EventArgs e) => _speech.OnHotkeyReleased();

        private void ShowSettings()
        {
            // A modal dialog does not block the tray icon, so the menu can ask for the window again
            if (_settingsForm != null)
            {
                _settingsForm.Activate();
                return;
            }

            // No dictation while the settings are open: its hotkey may be about to change. A recording in progress is
            // dropped; a transcription finishes and pastes as usual.
            _hotkeyService.SetSpeechHotkey(null);
            if (_speechState == SpeechState.Recording) _speech.Cancel();
            try
            {
                ShowSettingsDialog();
            }
            finally
            {
                // Back as it was if the settings were not saved (ApplySettings has set it otherwise)
                _hotkeyService.SetSpeechHotkey(_currentSettings.SpeechEnabled ? _currentSettings.SpeechHotkey : null);
            }
        }

        private void ShowSettingsDialog()
        {
            using var form = new SettingsForm(
                _currentSettings, ct => _hotkeyService.CaptureHotkeyAsync(ct), _recorder.GetInputDevices(), _downloads);
            _settingsForm = form;
            // Pausing or resuming while a hotkey is being recorded would leave the hook in the wrong state
            _pauseItem?.Enabled = false;

            DialogResult result;
            try
            {
                result = form.ShowDialog();
            }
            finally
            {
                _settingsForm = null;
                _pauseItem?.Enabled = true;
            }

            if (result != DialogResult.OK || form.UpdatedSettings is not { } updatedSettings) return;

            _currentSettings = updatedSettings;
            ApplySettings();
            ApplyLanguage();

            try
            {
                _settingsService.Save(_currentSettings);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not save settings", ex);
                MessageBox.Show(
                    Strings.SettingsNotSaved(ex.Message),
                    AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (_currentSettings.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(2000, AppName, Strings.BalloonSettingsSaved, ToolTipIcon.Info);
            }
        }

        private void ShowAbout()
        {
            string version = Strings.FormatVersion(typeof(TrayApplicationContext).Assembly.GetName().Version);
            DialogResult answer = MessageBox.Show(
                Strings.AboutText(version) + "\n\n" + Strings.AboutOpenReleasesPrompt,
                AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return;

            try
            {
                Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not open the releases page", ex);
            }
        }

        private void Exit()
        {
            _notifyIcon.Visible = false;
            _hotkeyService.Stop();
            _speech.Cancel();
            _downloads.Cancel(); // the partial file stays: the download resumes at the next start
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _orchestrator.ConversionFailed -= OnConversionFailed;
                _hotkeyService.HookFailed -= OnHookFailed;
                _hotkeyService.SpeechHotkeyPressed -= OnSpeechHotkeyPressed;
                _hotkeyService.SpeechHotkeyReleased -= OnSpeechHotkeyReleased;
                _speech.StateChanged -= OnSpeechStateChanged;
                _downloads.StateChanged -= OnDownloadStateChanged;
                _notifyIcon?.Dispose();
                _overlay.Dispose();
                _trayIcon?.Dispose();
                _recordingIcon?.Dispose();
                _transcribingIcon?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
