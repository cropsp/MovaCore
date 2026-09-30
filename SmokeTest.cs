using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using LayoutConverter.App.Models;
using LayoutConverter.App.Services;
using LayoutConverter.App.UI;

namespace LayoutConverter.App
{
    /// <summary>
    /// <c>MovaCore.exe --smoke-test</c>: runs the real tray app for a few seconds, exercises the code paths that Native
    /// AOT trimming can break (settings form, embedded resources, clipboard P/Invoke, converter, native hook library)
    /// and exits. CI runs it against the published executable; it passes only if no error was logged.
    /// It never simulates key presses: on a CI runner they would land in the job's console.
    /// </summary>
    internal static class SmokeTest
    {
        private const int StepIntervalMs = 1500;

        public static bool Completed { get; private set; }

        public static void Schedule(ApplicationContext context, IClipboardService clipboard, ILayoutConverterService converter)
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
            string converted = await converter.ConvertAsync("ghbdsn");
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
    }
}
