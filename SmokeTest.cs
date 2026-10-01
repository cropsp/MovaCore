using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;
using MovaCore.UI;

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
                            form = new SettingsForm(new AppSettings());
                            form.Show();
                            break;
                        case 1:
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
