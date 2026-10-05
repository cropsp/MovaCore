using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MovaCore.Models;
using MovaCore.Services;
using MovaCore.UI;

namespace MovaCore
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool smokeTest = args.Contains("--smoke-test");
            // A small whisper.cpp model and a recording of "hello world" for the smoke test to transcribe (CI provides them)
            string? smokeTestModel = ArgumentValue(args, "--smoke-test-model");
            string? smokeTestAudio = ArgumentValue(args, "--smoke-test-audio");

            AppLog.Initialize(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovaCore", "logs"));
            AppLog.Info($"MovaCore {typeof(Program).Assembly.GetName().Version} starting on " +
                $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})" +
                (smokeTest ? ", smoke test" : ""));
            RegisterExceptionHandlers(smokeTest);

            // Set up WinForms state
            ApplicationConfiguration.Initialize();

            // Until the settings are loaded, messages follow the Windows display language
            Strings.Language = WindowsLanguage.Resolve(UiLanguage.Auto);

            // Only one instance per user session: a second tray icon would be confusing,
            // and both instances would overwrite each other's settings.
            using var singleInstanceMutex = TryAcquireSingleInstance();
            if (singleInstanceMutex == null)
            {
                MessageBox.Show(
                    Strings.AlreadyRunning,
                    "MovaCore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!IsHookLibraryAvailable())
            {
                AppLog.Error("uiohook.dll was not found next to the executable");
                if (smokeTest)
                {
                    Environment.ExitCode = 1;
                    return;
                }
                MessageBox.Show(
                    Strings.HookLibraryMissing,
                    "MovaCore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Composition root: a handful of long-lived objects wired by hand (no DI container to trim under AOT).
            // The clipboard service creates its owner window here, on the UI thread whose message loop serves it.
            // Disposed in reverse order: dictation stops before the clipboard and the hook it uses go away.
            var converter = KeyboardLayouts.CreateConverter();
            var layouts = new KeyboardLayouts();
            using var hotkeys = new HotkeyService();
            using var clipboard = new ClipboardService();
            var clipboardGate = new ClipboardGate();
            var orchestrator = new HotkeyOrchestrator(hotkeys, converter, clipboard, layouts, clipboardGate);
            using var recorder = new WasapiAudioRecorder();
            using var recognizer = new WhisperSpeechRecognizer();
            using var dictationTarget = new WindowsDictationTarget(hotkeys);
            using var dictationContext = new DictationContext(dictationTarget);
            using var speech = new SpeechOrchestrator(
                recorder, recognizer, new TextPaster(hotkeys, clipboard, clipboardGate), recognizer, dictationContext);
            using var downloader = new ModelDownloader();
            using var downloads = new ModelDownloadManager(downloader, SpeechModelCatalog.DefaultModelsDirectory);
            var settings = new SettingsService(new StartupRegistration());
            using var context = new TrayApplicationContext(hotkeys, orchestrator, settings, speech, recorder, downloads);

            if (smokeTest)
            {
                SmokeTest.Schedule(
                    context, clipboard, converter, hotkeys, recorder, downloader, downloads, smokeTestModel, smokeTestAudio);
            }

            Application.Run(context);

            if (smokeTest)
            {
                bool passed = SmokeTest.Completed && AppLog.ErrorCount == 0;
                AppLog.Info(passed ? "Smoke test passed" : "Smoke test FAILED");
                Environment.ExitCode = passed ? 0 : 1;
            }
            AppLog.Info("MovaCore exited");
        }

        private static string? ArgumentValue(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static void RegisterExceptionHandlers(bool smokeTest)
        {
            // Exceptions on the UI thread (menu clicks, timers) are logged instead of silently killing the tray app
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLog.Error("Unhandled UI exception", e.Exception);
                if (smokeTest)
                {
                    Application.ExitThread();
                    return;
                }
                MessageBox.Show(
                    Strings.UnexpectedErrorWithLog(e.Exception.Message, AppLog.FilePath),
                    "MovaCore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                AppLog.Error("Unhandled exception, the app will close", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                AppLog.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }

        // SharpHook's native part is a separate uiohook.dll: Native AOT cannot embed it into the exe.
        // Without it the hook would only fail later on a background thread, so check up front.
        private static bool IsHookLibraryAvailable()
        {
            if (!NativeLibrary.TryLoad("uiohook", typeof(SharpHook.SimpleGlobalHook).Assembly, null, out IntPtr handle))
                return false;

            NativeLibrary.Free(handle);
            return true;
        }

        private static Mutex? TryAcquireSingleInstance()
        {
            try
            {
                var mutex = new Mutex(false, @"Local\MovaCore.SingleInstance", out bool createdNew);
                if (createdNew) return mutex;

                mutex.Dispose();
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex belongs to an instance running as administrator, which a regular process cannot open
                return null;
            }
        }
    }
}
