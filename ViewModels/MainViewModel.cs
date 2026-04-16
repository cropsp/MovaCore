using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LayoutConverter.App.Services;

namespace LayoutConverter.App.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IHotkeyService _hotkeyService;
        private readonly ILayoutConverterService _layoutConverterService;
        private readonly IClipboardService _clipboardService;

        public MainViewModel(
            IHotkeyService hotkeyService,
            ILayoutConverterService layoutConverterService,
            IClipboardService clipboardService)
        {
            _hotkeyService = hotkeyService;
            _layoutConverterService = layoutConverterService;
            _clipboardService = clipboardService;

            // Підписуємось на глобальний хоткей
            _hotkeyService.HotkeyTriggered += OnHotkeyTriggered;
        }

        private async void OnHotkeyTriggered(object sender, EventArgs e)
        {
            // 1. Імітуємо Ctrl+C
            _hotkeyService.SimulateCopy();
            await Task.Delay(150); // Чекаємо, поки ОС оновить буфер

            // 2. Переходимо в UI потік для роботи з буфером WinUI
            App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
            {
                var originalText = await _clipboardService.GetTextAsync();

                if (!string.IsNullOrEmpty(originalText))
                {
                    // 3. Конвертуємо розкладку
                    var convertedText = await _layoutConverterService.ConvertAsync(originalText);

                    // 4. Кладемо назад у буфер
                    await _clipboardService.SetTextAsync(convertedText);
                    await Task.Delay(100); // Чекаємо, поки буфер прийме дані

                    // 5. Імітуємо Ctrl+V
                    _hotkeyService.SimulatePaste();
                }
            });
        }

        [RelayCommand]
        public void ShowSettings()
        {
            // Показуємо вікно налаштувань (з трею)
            App.MainWindow?.Activate();
        }

        [RelayCommand]
        public void ExitApplication()
        {
            _hotkeyService.Stop();
            App.Current.Exit();
        }
    }
}
