using System.Threading.Tasks;
using System.Windows.Forms;

namespace LayoutConverter.App.Services
{
    public interface IClipboardService
    {
        Task<string> GetTextAsync();
        Task SetTextAsync(string text);
    }

    public class ClipboardService : IClipboardService
    {
        public Task<string> GetTextAsync()
        {
            if (Clipboard.ContainsText())
            {
                return Task.FromResult(Clipboard.GetText());
            }
            return Task.FromResult(string.Empty);
        }

        public Task SetTextAsync(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
            }
            return Task.CompletedTask;
        }
    }
}
