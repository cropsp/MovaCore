using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LayoutConverter.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LayoutConverter.App
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool smokeTest = args.Contains("--smoke-test");

            AppLog.Initialize(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MovaCore", "logs"));
            AppLog.Info($"MovaCore {typeof(Program).Assembly.GetName().Version} starting on " +
                $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})" +
                (smokeTest ? ", smoke test" : ""));
            RegisterExceptionHandlers(smokeTest);

            // Set up WinForms state
            ApplicationConfiguration.Initialize();

            // Only one instance per user session: a second tray icon would be confusing,
            // and both instances would overwrite each other's settings.
            using var singleInstanceMutex = TryAcquireSingleInstance();
            if (singleInstanceMutex == null)
            {
                MessageBox.Show(
                    "MovaCore is already running. Look for the mouse icon in the system tray.",
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
                    "uiohook.dll was not found next to MovaCore.exe, so the hotkey cannot work.\n\n" +
                    "Extract all files from the release archive into the same folder and start MovaCore again.",
                    "MovaCore",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Configure Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);

            using var serviceProvider = services.BuildServiceProvider();

            // Start the application context
            var context = serviceProvider.GetRequiredService<TrayApplicationContext>();
            if (smokeTest)
            {
                SmokeTest.Schedule(
                    context,
                    serviceProvider.GetRequiredService<IClipboardService>(),
                    serviceProvider.GetRequiredService<ILayoutConverterService>());
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
                    $"Unexpected error: {e.Exception.Message}\n\nDetails were written to {AppLog.FilePath ?? "the log"}.",
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

        private static void ConfigureServices(IServiceCollection services)
        {
            // Services
            services.AddSingleton<ILayoutConverterService, LayoutConverterService>();
            services.AddSingleton<IHotkeyService, HotkeyService>();
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<HotkeyOrchestrator>();

            // Application Context
            services.AddSingleton<TrayApplicationContext>();
        }
    }
}
