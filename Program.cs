using System;
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

            // Configure Dependency Injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            
            using var serviceProvider = services.BuildServiceProvider();

            // Start the application context
            var context = serviceProvider.GetRequiredService<TrayApplicationContext>();
            Application.Run(context);
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
