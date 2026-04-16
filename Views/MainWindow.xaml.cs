using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using LayoutConverter.App.ViewModels;

namespace LayoutConverter.App.Views
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            
            // Витягуємо MainViewModel з DI
            this.DataContext = App.Services.GetRequiredService<MainViewModel>();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
        }
    }
}
