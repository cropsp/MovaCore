using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
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
        private ToolStripMenuItem? _firstStepsItem;
        private ToolStripMenuItem? _aboutItem;
        private ToolStripMenuItem? _exitItem;

        private Form? _openDialog; // the settings or first steps window, if one is open: one at a time
        private bool _paused; // not persisted: MovaCore always starts active

        // Each hint is shown at most once a session (UI thread only)
        private bool _nothingSelectedHinted;
        private bool _slowRecognitionHinted;

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
            _orchestrator.NothingSelected += OnNothingSelected;
            _hotkeyService.HookFailed += OnHookFailed;
            _speech.StateChanged += OnSpeechStateChanged;
            _speech.TextPasted += OnTextPasted;
            _speech.RecognitionSlow += OnRecognitionSlow;
            _downloads.StateChanged += OnDownloadStateChanged;
            _downloads.ModelDeleted += OnModelDeleted;

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
                s.RestoreClipboard,
                s.SpeechFastRecognition,
                s.SpeechGpu));

            // Turning dictation on downloads the chosen model (and resumes an interrupted download at startup), unless
            // the user has just cancelled that download, or deleted the model, in the settings
            ModelDownloadState download = _downloads.State;
            if (s.SpeechEnabled && SpeechModelCatalog.Selected(s.SpeechModel) is { } model
                && !(download.Status == ModelDownloadStatus.Cancelled && download.ModelId == model.Id)
                && !_downloads.WasDeleted(model))
            {
                _downloads.Start(model);
            }
        }

        // A deleted model that dictation was using is freed from memory at once, not when the settings are saved
        private void OnModelDeleted(object? sender, SpeechModelInfo model)
        {
            _uiContext.Post(_ =>
            {
                ApplySpeechSettings();
                UpdateTrayStatus();
            }, null);
        }

        private void ApplyLanguage()
        {
            Strings.Language = WindowsLanguage.Resolve(_currentSettings.Language);
            UpdateTrayTexts();
        }

        // Does nothing while the tray icon does not exist yet: the constructor applies the language first
        private void UpdateTrayTexts()
        {
            if (_settingsItem == null || _pauseItem == null || _firstStepsItem == null || _aboutItem == null || _exitItem == null)
                return;

            _settingsItem.Text = Strings.MenuSettings;
            _pauseItem.Text = Strings.MenuPause;
            _pauseItem.Checked = _paused;
            _firstStepsItem.Text = Strings.MenuFirstSteps;
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

        // Raised on a worker thread. New users press the hotkey before selecting anything, and nothing seems to happen.
        private void OnNothingSelected(object? sender, EventArgs e)
        {
            _uiContext.Post(_ =>
            {
                if (_nothingSelectedHinted || !_currentSettings.ShowNotifications) return;
                _nothingSelectedHinted = true;
                _notifyIcon.ShowBalloonTip(
                    4000, AppName, Strings.HintNothingSelected(_currentSettings.Trigger.ToString()), ToolTipIcon.Info);
            }, null);
        }

        // Raised on a worker thread
        private void OnRecognitionSlow(object? sender, EventArgs e) => _uiContext.Post(_ => ShowRecognitionAdvice(), null);

        private void ShowRecognitionAdvice()
        {
            if (_slowRecognitionHinted || !_currentSettings.ShowNotifications) return;
            RecognitionAdvice advice = RecognitionAdvisor.Choose(
                _currentSettings.SpeechFastRecognition,
                _currentSettings.SpeechUseGpu,
                VulkanDevices.List().Count,
                WhisperSpeechRecognizer.ProcessorRuntimeLoaded,
                RuntimeInformation.ProcessArchitecture != Architecture.Arm64);
            if (advice == RecognitionAdvice.None) return;

            _slowRecognitionHinted = true;
            AppLog.Info($"Dictation: recognition is slow, advised {advice}");
            _notifyIcon.ShowBalloonTip(8000, AppName, Strings.RecognitionAdviceText(advice), ToolTipIcon.Info);
        }

        // Raised on a worker thread
        private void OnSpeechStateChanged(object? sender, SpeechStateChangedEventArgs e)
        {
            _uiContext.Post(_ => ShowSpeechState(e), null);
        }

        // The indicator goes the moment the text appears, not after the clipboard is restored
        private void OnTextPasted(object? sender, EventArgs e)
        {
            _uiContext.Post(_ => _overlay.HideOverlay(), null);
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
                _overlay.ShowMessage(message, e.Outcome switch
                {
                    SpeechOutcome.NoSpeech => MouseScene.Pose.Puzzled,
                    SpeechOutcome.NoSignal => MouseScene.Pose.Straining,
                    _ => MouseScene.Pose.Calm,
                });
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

            // Downloaded from the first steps window, before voice input is on: that window says it itself
            if (state.Status == ModelDownloadStatus.Completed && state.ModelId == _currentSettings.SpeechModel
                && _currentSettings.SpeechEnabled)
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
            _firstStepsItem = new ToolStripMenuItem(Strings.MenuFirstSteps, null, (s, e) => ShowFirstSteps());
            _aboutItem = new ToolStripMenuItem(Strings.MenuAbout, null, (s, e) => ShowAbout());
            _exitItem = new ToolStripMenuItem(Strings.MenuExit, null, (s, e) => Exit());

            menu.Items.Add(_settingsItem);
            menu.Items.Add(_pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_firstStepsItem);
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

        private void ShowSettings() => ShowDialog(() =>
            new SettingsForm(
                _currentSettings, ct => _hotkeyService.CaptureHotkeyAsync(ct), _recorder.GetInputDevices(), VulkanDevices.List(),
                _downloads),
            OnSettingsClosed);

        private void ShowFirstSteps()
        {
            bool firstRun = _settingsService.IsFirstRun;
            ShowDialog(
                () => new FirstStepsForm(_currentSettings, ct => _hotkeyService.CaptureHotkeyAsync(ct), _downloads, firstRun),
                form => OnFirstStepsClosed((FirstStepsForm)form, firstRun));
        }

        /// <summary>At the first run, once the tray icon is there: what MovaCore does and how to start.</summary>
        public void ShowFirstStepsSoon() => _uiContext.Post(_ => ShowFirstSteps(), null);

        /// <summary>Shows the settings or the first steps, one window at a time, and hands it to <paramref name="closed"/>.</summary>
        private void ShowDialog(Func<Form> create, Action<Form> closed)
        {
            // A modal dialog does not block the tray icon, so the menu can ask for a window again
            if (_openDialog != null)
            {
                _openDialog.Activate();
                return;
            }

            // No dictation while the window is open: its hotkey may be about to change. A recording in progress is
            // dropped; a transcription finishes and pastes as usual.
            _hotkeyService.SetSpeechHotkey(null);
            if (_speechState == SpeechState.Recording) _speech.Cancel();
            try
            {
                using Form form = create();
                _openDialog = form;
                // Pausing or resuming while a hotkey is being recorded would leave the hook in the wrong state
                _pauseItem?.Enabled = false;
                try
                {
                    form.ShowDialog();
                }
                finally
                {
                    _openDialog = null;
                    _pauseItem?.Enabled = true;
                }
                closed(form);
            }
            finally
            {
                // Back as it was if the settings were not saved (ApplySettings has set it otherwise)
                _hotkeyService.SetSpeechHotkey(_currentSettings.SpeechEnabled ? _currentSettings.SpeechHotkey : null);
            }
        }

        private void OnSettingsClosed(Form form)
        {
            if (form.DialogResult != DialogResult.OK || ((SettingsForm)form).UpdatedSettings is not { } updatedSettings) return;

            bool gpuChosen = updatedSettings.SpeechUseGpu && !_currentSettings.SpeechUseGpu;
            if (!ApplyAndSave(updatedSettings)) return;

            if (_currentSettings.ShowNotifications)
            {
                _notifyIcon.ShowBalloonTip(2000, AppName, Strings.BalloonSettingsSaved, ToolTipIcon.Info);
            }

            // From Processor only to a graphics card: the processor's runtime stays loaded until MovaCore restarts
            if (gpuChosen && _currentSettings.SpeechEnabled && WhisperSpeechRecognizer.ProcessorRuntimeLoaded
                && VulkanDevices.List().Count > 0
                && MessageBox.Show(Strings.RestartForGpuPrompt, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    == DialogResult.Yes)
            {
                Restart();
            }
        }

        private void OnFirstStepsClosed(FirstStepsForm form, bool firstRun)
        {
            if (form.UpdatedSettings is not { } updated) return;

            // Saved at the first run even unchanged: the settings file marks that the first steps were shown
            bool changed = updated.LaunchAtStartup != _currentSettings.LaunchAtStartup
                || updated.SpeechEnabled != _currentSettings.SpeechEnabled
                || updated.SpeechHotkey != _currentSettings.SpeechHotkey;
            if ((changed || firstRun) && !ApplyAndSave(updated)) return;

            if (firstRun && _currentSettings.ShowNotifications)
                _notifyIcon.ShowBalloonTip(5000, AppName, Strings.BalloonFirstRun, ToolTipIcon.Info);
        }

        /// <summary>Applies the settings and saves them; false (after telling the user) if they could not be saved.</summary>
        private bool ApplyAndSave(AppSettings settings)
        {
            _currentSettings = settings;
            ApplySettings();
            ApplyLanguage();

            try
            {
                _settingsService.Save(_currentSettings);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not save settings", ex);
                MessageBox.Show(
                    Strings.SettingsNotSaved(ex.Message),
                    AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Starts another MovaCore and exits. The new one waits for this one to be gone (Program: --restarted-from), or the
        /// single-instance check would turn it away.
        /// </summary>
        private void Restart()
        {
            try
            {
                string path = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unknown");
                Process.Start(new ProcessStartInfo(path, $"--restarted-from {Environment.ProcessId}") { UseShellExecute = false })
                    ?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not restart MovaCore", ex);
                MessageBox.Show(Strings.RestartFailed(ex.Message), AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            AppLog.Info("Restarting");
            Exit();
        }

        private void ShowAbout()
        {
            string version = Strings.FormatVersion(typeof(TrayApplicationContext).Assembly.GetName().Version);
            DialogResult answer = MessageBox.Show(
                Strings.AboutText(version, Strings.BuildLabel(BuildInfo.Number, BuildInfo.Commit, BuildInfo.Date))
                    + "\n\n" + Strings.AboutOpenReleasesPrompt,
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
                _orchestrator.NothingSelected -= OnNothingSelected;
                _hotkeyService.HookFailed -= OnHookFailed;
                _hotkeyService.SpeechHotkeyPressed -= OnSpeechHotkeyPressed;
                _hotkeyService.SpeechHotkeyReleased -= OnSpeechHotkeyReleased;
                _speech.StateChanged -= OnSpeechStateChanged;
                _speech.TextPasted -= OnTextPasted;
                _speech.RecognitionSlow -= OnRecognitionSlow;
                _downloads.StateChanged -= OnDownloadStateChanged;
                _downloads.ModelDeleted -= OnModelDeleted;
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
