using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace LayoutConverter.App.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _hotkey = "Ctrl+Alt+C";

        [ObservableProperty]
        private string _selectedLanguagePair = "EN -> UA";

        public ObservableCollection<string> LanguagePairs { get; } = new()
        {
            "EN -> UA",
            "UA -> EN",
            "EN -> RU",
            "RU -> EN"
        };

        [RelayCommand]
        private void SaveSettings()
        {
            // Placeholder for saving settings
        }
    }
}
