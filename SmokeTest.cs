using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;
using MovaCore.UI;
using SharpHook;
using SharpHook.Data;
using Whisper.net.Wave;

namespace MovaCore
{
    /// <summary>
    /// <c>MovaCore.exe --smoke-test</c>: runs the real tray app for a few seconds, exercises the code paths that Native
    /// AOT trimming can break or that only work on real Windows (settings form, embedded resources, clipboard P/Invoke and
    /// delayed rendering, converter, native hook library) and exits. CI runs it against the published executable; it
    /// passes only if no error was logged.
    /// It never simulates key presses: on a CI runner they would land in the job's console.
    /// Dictation: the microphone list, the recording indicator, the file dialog, the Whisper runtime (and a real
    /// transcription when CI passes a model and a recording) and the model download's network path.
    /// </summary>
    internal static partial class SmokeTest
    {
        private const int StepIntervalMs = 1500;
        private const uint WM_CLOSE = 0x0010;

        private static IntPtr _dialogToClose; // found by the EnumThreadWindows callback

        public static bool Completed { get; private set; }

        public static void Schedule(
            ApplicationContext context,
            IClipboardService clipboard,
            ILayoutConverterService converter,
            IHotkeyService hotkeys,
            IAudioRecorder recorder,
            ModelDownloader downloader,
            ModelDownloadManager downloads,
            string? modelPath,
            string? audioPath)
        {
            var timer = new System.Windows.Forms.Timer { Interval = StepIntervalMs };
            SettingsForm? form = null;
            int step = 0;

            timer.Tick += async (s, e) =>
            {
                timer.Stop();
                try
                {
                    switch (step++)
                    {
                        case 0:
                            IReadOnlyList<AudioInputDevice> microphones = recorder.GetInputDevices();
                            // CI runners have no microphone: listing them must work, finding none is fine
                            AppLog.Info($"Smoke test: {microphones.Count} microphone(s) found");
                            recorder.Close(); // closing a microphone that is not open does nothing
                            if (recorder.IsOpen || recorder.CopyRecent(new float[SpectrumAnalyzer.WindowSize]) != 0)
                                AppLog.Error("Smoke test: a closed recorder reports audio");
                            form = new SettingsForm(new AppSettings(), _ => Task.FromResult<Hotkey?>(null), microphones, downloads);
                            form.Show();
                            break;
                        case 1:
                            // Controls on a tab page are created when the page is first shown
                            for (int i = form!.Tabs.TabCount - 1; i >= 0; i--) form.Tabs.SelectedIndex = i;
                            CheckSettingsFormSize(form);
                            await CheckConverterAndClipboardAsync(clipboard, converter);
                            break;
                        case 2:
                            await CheckClipboardRestoreAsync(clipboard);
                            break;
                        case 3:
                            // Stop must keep the hook reusable; a failed restart is logged through HookFailed
                            hotkeys.Stop();
                            hotkeys.Start();
                            break;
                        case 4:
                            CheckKeyboardLayouts();
                            break;
                        case 5:
                            await CheckHotkeyCaptureAsync(hotkeys);
                            break;
                        case 6:
                            await CheckRecordingIndicatorAsync();
                            CheckIcons();
                            await CheckDictationTargetAsync(hotkeys);
                            break;
                        case 7:
                            CheckFileDialog(form!);
                            break;
                        case 8:
                            await CheckSpeechRecognitionAsync(modelPath, audioPath);
                            break;
                        case 9:
                            await CheckModelDownloadAsync(downloader);
                            CheckModelDeletion(downloader);
                            break;
                        default:
                            form?.Close();
                            form?.Dispose();
                            timer.Dispose();
                            Completed = true;
                            context.ExitThread();
                            return;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Smoke test step {step - 1} failed", ex);
                    context.ExitThread();
                    return;
                }
                timer.Start();
            };
            timer.Start();
        }

        // The settings form sizes itself; at 150 % it must still fit a 1080p screen (720 logical pixels high)
        private static void CheckSettingsFormSize(Form form)
        {
            int logicalHeight = form.Height * 96 / form.DeviceDpi;
            int logicalWidth = form.Width * 96 / form.DeviceDpi;
            AppLog.Info($"Smoke test: settings form is {logicalWidth}x{logicalHeight} at 96 DPI");
            if (logicalHeight > 720)
                AppLog.Error($"Smoke test: the settings form is {logicalHeight} px high and would not fit a 1080p screen at 150 %");
        }

        // The converter reads the user's installed layouts; check that reading against the real layout files.
        // "Ukrainian (Enhanced)" must reproduce the built-in table; the older "Ukrainian" must convert correctly too.
        private static void CheckKeyboardLayouts()
        {
            var installedBefore = new HashSet<IntPtr>(KeyboardLayouts.GetInstalled());
            IntPtr us = KeyboardLayouts.Load("00000409");
            IntPtr ukrainian = KeyboardLayouts.Load("00000422");
            IntPtr enhanced = KeyboardLayouts.Load("00020422");
            try
            {
                if (us == IntPtr.Zero || ukrainian == IntPtr.Zero || enhanced == IntPtr.Zero)
                {
                    AppLog.Error("Smoke test: could not load the US and Ukrainian keyboard layouts");
                    return;
                }

                var enhancedPairs = new HashSet<(char, char)>(KeyboardLayouts.ReadKeyPairs(us, enhanced));
                var standardPairs = KeyboardLayouts.ReadKeyPairs(us, ukrainian);
                var mismatches = new List<string>();
                for (int i = 0; i < LayoutConverterService.DefaultEnglishKeys.Length; i++)
                {
                    char en = LayoutConverterService.DefaultEnglishKeys[i];
                    char ua = LayoutConverterService.DefaultUkrainianKeys[i];
                    if (enhancedPairs.Contains((en, ua))) continue;

                    // Show what the real layout types on that key, as code points (some are look-alike characters)
                    var actual = new List<string>();
                    foreach (var (pairEn, pairUa) in enhancedPairs)
                    {
                        if (pairEn == en) actual.Add($"U+{(int)pairUa:X4}");
                    }
                    mismatches.Add($"{en}->{ua} (layout types {(actual.Count > 0 ? string.Join("/", actual) : "nothing")})");
                }
                if (mismatches.Count > 0)
                    AppLog.Error("Smoke test: built-in pairs not typed by the real US/Ukrainian (Enhanced) layouts: " +
                        string.Join("; ", mismatches));

                var (englishKeys, ukrainianKeys) = LayoutTableBuilder.Build(standardPairs);
                var standardConverter = new LayoutConverterService(englishKeys, ukrainianKeys);
                if (standardConverter.Convert("ghbdsn") != "привіт" || standardConverter.Convert("`") != "ё"
                    || standardConverter.Convert("\\") != "\\")
                    AppLog.Error("Smoke test: the tables read from the \"Ukrainian\" layout convert incorrectly");

                AppLog.Info($"Smoke test: layouts read ({enhancedPairs.Count} enhanced, {standardPairs.Count} standard pairs)");
            }
            finally
            {
                foreach (IntPtr layout in new[] { us, ukrainian, enhanced })
                {
                    if (layout != IntPtr.Zero && !installedBefore.Contains(layout)) KeyboardLayouts.Unload(layout);
                }
            }

            bool switched = new KeyboardLayouts().SwitchForegroundWindowTo(KeyboardLanguage.English);
            AppLog.Info($"Smoke test: layout switch request {(switched ? "sent" : "not sent")}");
        }

        // Recording a hotkey goes through the global hook. A simulated F13 (bound to nothing) must be captured and
        // swallowed. Simulating it is safe: no application reacts to F13.
        private static async Task CheckHotkeyCaptureAsync(IHotkeyService hotkeys)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Task<Hotkey?> capture = hotkeys.CaptureHotkeyAsync(timeout.Token);

            var simulator = new EventSimulator();
            simulator.SimulateKeyPress(KeyCode.VcF13);
            simulator.SimulateKeyRelease(KeyCode.VcF13);

            Hotkey? captured = await capture;
            if (captured != new Hotkey(KeyCode.VcF13, HotkeyModifiers.None))
                AppLog.Error($"Smoke test: recording a hotkey returned {captured?.ToString() ?? "nothing"} instead of F13");
            else
                AppLog.Info("Smoke test: hotkey recording checked");
        }

        // The indicator must never take the focus: the recognized text goes to the window the user is typing in. Its
        // equalizer must react to sound, and each state must paint.
        private static async Task CheckRecordingIndicatorAsync()
        {
            IntPtr foregroundBefore = GetForegroundWindow();
            bool sound = false;
            using var overlay = new RecordingOverlay(buffer =>
            {
                if (!sound)
                {
                    Array.Clear(buffer);
                    return 0; // the microphone is still opening
                }
                for (int i = 0; i < buffer.Length; i++) buffer[i] = 0.1f * MathF.Sin(2 * MathF.PI * 700 * i / AudioSamples.SampleRate);
                return buffer.Length;
            });

            overlay.ShowRecording();
            if (!overlay.Visible) AppLog.Error("Smoke test: the recording indicator did not show");
            await Task.Delay(250);
            using Bitmap waiting = overlay.RenderFrame();
            sound = true;
            await Task.Delay(400);
            using Bitmap speaking = overlay.RenderFrame();
            if (SameImage(waiting, speaking)) AppLog.Error("Smoke test: the recording indicator did not react to sound");
            CheckCapsuleShape(overlay, speaking);

            overlay.ShowTranscribing();
            await Task.Delay(450); // the mouse gnaws its wheat after a delay
            overlay.RenderFrame().Dispose();
            overlay.ShowPasted();
            await Task.Delay(150);
            overlay.RenderFrame().Dispose();
            foreach (MouseScene.Pose pose in new[] { MouseScene.Pose.Puzzled, MouseScene.Pose.Straining, MouseScene.Pose.Calm })
            {
                overlay.ShowMessage(Strings.OverlayNoSignal, pose);
                await Task.Delay(150);
                overlay.RenderFrame().Dispose();
            }
            if (GetForegroundWindow() != foregroundBefore) AppLog.Error("Smoke test: the recording indicator took the focus");

            overlay.HideOverlay();
            await Task.Delay(500); // it fades out
            if (overlay.Visible) AppLog.Error("Smoke test: the recording indicator did not hide");
            AppLog.Info("Smoke test: recording indicator checked");
        }

        // The next phrase continues the previous dictation only until the user clicks: raw input must report a click
        // while watched, and only then
        private static async Task CheckDictationTargetAsync(IHotkeyService hotkeys)
        {
            using var target = new WindowsDictationTarget(hotkeys);
            AppLog.Info($"Smoke test: focus {(target.GetFocus() is { } focus ? $"window {focus.Window:x}, control {focus.Control:x}" : "unknown")}");
            int clicks = 0;
            target.Interrupted += (_, _) => clicks++;
            var simulator = new EventSimulator();

            target.WatchClicks(true);
            await Task.Delay(200);
            simulator.SimulateMousePress(MouseButton.Button3); // the middle button: no click lands anywhere that matters
            simulator.SimulateMouseRelease(MouseButton.Button3);
            await Task.Delay(300);
            if (clicks == 0) AppLog.Error("Smoke test: a mouse click was not noticed");

            target.WatchClicks(false);
            await Task.Delay(200);
            int seen = clicks;
            simulator.SimulateMousePress(MouseButton.Button3);
            simulator.SimulateMouseRelease(MouseButton.Button3);
            await Task.Delay(300);
            if (clicks != seen) AppLog.Error("Smoke test: mouse clicks were still watched after stopping");
            AppLog.Info("Smoke test: click watching checked");
        }

        // The indicator is a capsule with a transparent background: clear just inside the window's corner, where only a
        // rectangle would reach, and solid in the middle and inside the rounded end
        private static void CheckCapsuleShape(Control overlay, Bitmap frame)
        {
            int margin = overlay.LogicalToDeviceUnits(6);
            int corner = frame.GetPixel(margin + 2, margin + 2).A;
            int middle = frame.GetPixel(frame.Width / 2, frame.Height / 2).A;
            int roundEnd = frame.GetPixel(margin + overlay.LogicalToDeviceUnits(4), frame.Height / 2).A;
            if (corner != 0 || middle != 255 || roundEnd != 255)
                AppLog.Error($"Smoke test: the recording indicator is not a capsule (corner {corner}, middle {middle}, end {roundEnd})");
            else
                AppLog.Info("Smoke test: the recording indicator is a capsule");
        }

        private static bool SameImage(Bitmap a, Bitmap b)
        {
            if (a.Size != b.Size) return false;
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
                }
            }
            return true;
        }

        private static void CheckIcons()
        {
            Size size = SystemInformation.SmallIconSize;
            Icon?[] icons = { AppResources.LoadIcon(size), AppResources.LoadRecordingIcon(size), AppResources.LoadTranscribingIcon(size) };
            foreach (Icon? icon in icons)
            {
                if (icon == null) AppLog.Error("Smoke test: an embedded tray icon is missing");
                icon?.Dispose();
            }
        }

        // The model's Browse button opens a common file dialog (COM under the hood); a timer inside its modal loop
        // closes it again
        private static void CheckFileDialog(Form owner)
        {
            using var dialog = new OpenFileDialog { Filter = Strings.SpeechModelFileFilter };
            using var closer = new System.Windows.Forms.Timer { Interval = 1000 };
            bool closed = false;
            closer.Tick += (_, _) =>
            {
                _dialogToClose = IntPtr.Zero;
                unsafe
                {
                    EnumThreadWindows(GetCurrentThreadId(), &FindDialog, IntPtr.Zero);
                }
                if (_dialogToClose == IntPtr.Zero) return;
                PostMessage(_dialogToClose, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                closed = true;
                closer.Stop();
            };
            closer.Start();
            dialog.ShowDialog(owner);

            if (closed)
                AppLog.Info("Smoke test: file dialog opened and closed");
            else
                AppLog.Error("Smoke test: the file dialog did not open");
        }

        [UnmanagedCallersOnly]
        private static unsafe int FindDialog(IntPtr window, IntPtr parameter)
        {
            // #32770 is the window class of dialog boxes, the common file dialog included
            const int capacity = 16;
            char* name = stackalloc char[capacity];
            int length = GetClassNameW(window, name, capacity);
            if (IsWindowVisible(window) && new ReadOnlySpan<char>(name, length).SequenceEqual("#32770"))
            {
                _dialogToClose = window;
                return 0; // stop
            }
            return 1;
        }

        private static async Task CheckSpeechRecognitionAsync(string? modelPath, string? audioPath)
        {
            // CI runners have AVX2: CpuUnsupported there means the processor check is wrong
            try
            {
                AppLog.Info("Smoke test: speech runtime " + WhisperSpeechRecognizer.LoadRuntime(useGpu: true));
            }
            catch (SpeechException ex)
            {
                AppLog.Error($"Smoke test: the speech runtime did not load ({ex.Error}): {ex.Message}");
                return;
            }

            // A recording of "hello world" if CI could make one, a second of a tone otherwise
            float[] audio;
            if (audioPath != null)
            {
                using FileStream wave = File.OpenRead(audioPath);
                audio = new WaveParser(wave).GetAvgSamples();
            }
            else
            {
                audio = new float[AudioSamples.SampleRate];
                for (int i = 0; i < audio.Length; i++) audio[i] = 0.2f * MathF.Sin(2 * MathF.PI * 440 * i / AudioSamples.SampleRate);
            }

            using var recognizer = new WhisperSpeechRecognizer();
            await CheckVoiceActivityDetectionAsync(recognizer, audioPath != null ? audio : null);

            if (modelPath == null)
            {
                AppLog.Info("Smoke test: no model given (--smoke-test-model), transcription not checked");
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var watch = Stopwatch.StartNew();
            IReadOnlyList<string> segments = await recognizer.TranscribeAsync(
                AudioSamples.PadToMinimum(audio, SpeechOrchestrator.MinAudioLength),
                new SpeechOptions(modelPath, "en", UseGpu: true),
                timeout.Token);
            // Synthetic audio, so the text may be logged
            string text = TranscriptText.Clean(segments);
            AppLog.Info($"Smoke test: transcribed in {watch.Elapsed.TotalSeconds:0.0} s: \"{text}\"");
            if (audioPath != null && !text.Contains("hello", StringComparison.OrdinalIgnoreCase))
                AppLog.Error("Smoke test: the recording of \"hello world\" was not recognized");
        }

        // The Silero model ships with the app: speech must be found in the recording and none in silence
        private static async Task CheckVoiceActivityDetectionAsync(WhisperSpeechRecognizer recognizer, float[]? speech)
        {
            if (!File.Exists(WhisperSpeechRecognizer.VadModelPath))
            {
                AppLog.Error($"Smoke test: the voice activity model is missing ({WhisperSpeechRecognizer.VadModelPath})");
                return;
            }

            var options = new SpeechOptions("", "en", UseGpu: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IReadOnlyList<SpeechSegment>? silence = await recognizer.DetectSpeechAsync(
                new float[AudioSamples.SampleRate * 2], options, timeout.Token);
            if (silence is not { Count: 0 })
                AppLog.Error($"Smoke test: voice activity detection found speech in silence ({silence?.Count.ToString() ?? "no detector"})");

            if (speech == null)
            {
                AppLog.Info("Smoke test: voice activity detection loaded; no recording to check it on");
                return;
            }
            IReadOnlyList<SpeechSegment>? segments = await recognizer.DetectSpeechAsync(speech, options, timeout.Token);
            if (segments is not { Count: > 0 })
            {
                AppLog.Error("Smoke test: voice activity detection found no speech in the recording");
                return;
            }
            float[] kept = AudioSamples.KeepSegments(speech, segments);
            AppLog.Info($"Smoke test: voice activity detection kept {AudioSamples.Duration(kept.Length).TotalSeconds:0.00} s " +
                $"of {AudioSamples.Duration(speech.Length).TotalSeconds:0.00} s in {segments.Count} segment(s)");
        }

        // Deleting a downloaded model (the Delete button), on a copy in a temporary folder
        private static void CheckModelDeletion(ModelDownloader downloader)
        {
            string folder = Path.Combine(Path.GetTempPath(), "MovaCore-smoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var manager = new ModelDownloadManager(downloader, folder);
                SpeechModelInfo model = SpeechModelCatalog.Find(SpeechModelCatalog.DefaultId)!;
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(manager.PathOf(model), "lmgg"u8.ToArray());
                manager.Delete(model);
                if (manager.IsDownloaded(model) || !manager.WasDeleted(model))
                    AppLog.Error("Smoke test: the model was not deleted");
                else
                    AppLog.Info("Smoke test: model deletion checked");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }

        // A 4 KB range request for the default model: TLS, the redirect to the CDN and the hash header, in the
        // published exe. Without network access this is not an error.
        private static async Task CheckModelDownloadAsync(ModelDownloader downloader)
        {
            SpeechModelInfo model = SpeechModelCatalog.Find(SpeechModelCatalog.DefaultId)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            RemoteFileProbe probe;
            try
            {
                probe = await downloader.ProbeAsync(SpeechModelCatalog.DownloadUrl(model), 4096, timeout.Token);
            }
            catch (Exception ex) when (ex is ModelDownloadException or OperationCanceledException)
            {
                AppLog.Info($"Smoke test: model download not checked (no network?): {ex.Message}");
                return;
            }

            if (!probe.Prefix.AsSpan().StartsWith("lmgg"u8))
                AppLog.Error("Smoke test: the model download is not a whisper.cpp model");
            // Without the hash, a download of this model would be checked by size and format only
            if (probe.Sha256 == null)
                AppLog.Error("Smoke test: Hugging Face did not report the SHA-256 of the default model");
            AppLog.Info($"Smoke test: model download reachable: {model.FileName}, " +
                $"{probe.Size?.ToString() ?? "unknown"} bytes, SHA-256 {probe.Sha256 ?? "unknown"}");
        }

        private static async Task CheckConverterAndClipboardAsync(IClipboardService clipboard, ILayoutConverterService converter)
        {
            string converted = converter.Convert("ghbdsn");
            if (converted != "привіт")
                AppLog.Error("Smoke test: unexpected conversion result");

            uint sequenceBefore = clipboard.GetSequenceNumber();
            if (!await clipboard.TrySetTextAsync(converted))
            {
                AppLog.Error("Smoke test: could not write to the clipboard");
                return;
            }
            if (clipboard.GetSequenceNumber() == sequenceBefore)
                AppLog.Error("Smoke test: the clipboard sequence number did not change");
            if (await clipboard.TryGetTextAsync() != converted)
                AppLog.Error("Smoke test: the clipboard returned different text");

            AppLog.Info("Smoke test: converter and clipboard checked");
        }

        // The same sequence as a conversion: capture, put the converted text with delayed rendering, let "the target
        // app" read it from another thread (which makes Windows send WM_RENDERFORMAT to our window), then restore.
        private static async Task CheckClipboardRestoreAsync(IClipboardService clipboard)
        {
            const string original = "MovaCore smoke test: original";
            const string converted = "MovaCore smoke test: converted";

            if (!await clipboard.TrySetTextAsync(original))
            {
                AppLog.Error("Smoke test: could not write the original text");
                return;
            }
            ClipboardSnapshot? snapshot = await clipboard.TryCaptureAsync();
            if (snapshot == null || snapshot.Items.Count == 0)
            {
                AppLog.Error("Smoke test: could not capture the clipboard");
                return;
            }

            long since = Stopwatch.GetTimestamp();
            if (!await clipboard.TrySetTextAsync(converted))
            {
                AppLog.Error("Smoke test: could not write the converted text");
                return;
            }
            uint sequenceBeforeRead = clipboard.GetSequenceNumber();
            if (await Task.Run(clipboard.TryGetTextAsync) != converted)
                AppLog.Error("Smoke test: the delayed-rendered text was not delivered");
            if (!await clipboard.WaitForTextReadAsync(since, TimeSpan.FromSeconds(2)))
                AppLog.Error("Smoke test: reading the converted text was not detected");
            AppLog.Info("Smoke test: rendering the promised text " +
                (clipboard.GetSequenceNumber() == sequenceBeforeRead ? "kept" : "changed") + " the sequence number");

            if (!await clipboard.TryRestoreAsync(snapshot, expectedSequence: 0))
                AppLog.Error("Smoke test: the clipboard was not restored");
            else if (await clipboard.TryGetTextAsync() != original)
                AppLog.Error("Smoke test: the restored clipboard holds different text");

            AppLog.Info("Smoke test: clipboard capture, paste detection and restore checked");
        }

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool EnumThreadWindows(uint threadId, delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr parameter);

        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
        private static unsafe partial int GetClassNameW(IntPtr window, char* className, int maxCount);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindowVisible(IntPtr window);

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [LibraryImport("kernel32.dll")]
        private static partial uint GetCurrentThreadId();
    }
}
