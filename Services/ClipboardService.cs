using System.Threading.Tasks;

namespace LayoutConverter.App.Services
{
    public interface IClipboardService
    {
        Task<string> GetTextAsync();
        Task SetTextAsync(string text);
    }

    public class ClipboardService : IClipboardService
    {
        public async Task<string> GetTextAsync()
        {
            // WinUI 3 Specific Clipboard logic
            var content = Microsoft.Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content.Contains(Microsoft.Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                return await content.GetTextAsync();
            }
            return string.Empty;
        }

        public Task SetTextAsync(string text)
        {
            var package = new Microsoft.Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Microsoft.Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return Task.CompletedTask;
        }
    }
}
