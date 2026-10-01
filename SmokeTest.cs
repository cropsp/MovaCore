using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;
using MovaCore.UI;
using SharpHook;
using SharpHook.Data;

namespace MovaCore
{
    /// <summary>
    /// <c>MovaCore.exe --smoke-test</c>: runs the real tray app for a few seconds, exercises the code paths that Native
    /// AOT trimming can break or that only work on real Windows (settings form, embedded resources, clipboard P/Invoke and
    /// delayed rendering, converter, native hook library) and exits. CI runs it against the published executable; it
    /// passes only if no error was logged.
    /// It never simulates key presses: on a CI runner they would land in the job's console.
    /// </summary>
    internal static class SmokeTest
    {
        private const int StepIntervalMs = 1500;

        public static bool Completed { get; private set; }

        public static void Schedule(
            ApplicationContext context,
            IClipboardService clipboard,
            ILayoutConverterService converter,
            IHotkeyService hotkeys)
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
                            form = new SettingsForm(new AppSettings(), _ => Task.FromResult<Hotkey?>(null));
                            form.Show();
                            break;
                        case 1:
                            CheckSettingsFormSize(form!);
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
    }
}
