using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using LayoutConverter.App.Services;

namespace LayoutConverter.App
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
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

            // Configure Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            
            using var serviceProvider = services.BuildServiceProvider();

            // Start the application context
            var context = serviceProvider.GetRequiredService<TrayApplicationContext>();
            Application.Run(context);
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
