using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using LayoutConverter.App.Services;
using LayoutConverter.App.ViewModels;
using LayoutConverter.App.Views;
using System;
using H.NotifyIcon;

namespace LayoutConverter.App
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }
        public static Window MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Initialize the Hotkey Service
            var hotkeyService = Services.GetRequiredService<IHotkeyService>();
            hotkeyService.Start();

            // Initialize MainWindow but don't show it (start minimized)
            MainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow.Closed += (s, args) => { MainWindow = null; };
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<ILayoutConverterService, LayoutConverterService>();
            services.AddSingleton<IHotkeyService, HotkeyService>();
            services.AddSingleton<IClipboardService, ClipboardService>();

            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<SettingsViewModel>();

            // Windows
            services.AddTransient<MainWindow>();

            return services.BuildServiceProvider();
        }

        private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
        {
            if (MainWindow == null)
            {
                MainWindow = Services.GetRequiredService<MainWindow>();
                MainWindow.Closed += (s, args) => { MainWindow = null; };
            }
            MainWindow.Activate();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            MainWindow?.Close();
            Exit();
        }
    }
}
