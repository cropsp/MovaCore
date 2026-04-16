using System;
using System.Threading;
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
            string result = string.Empty;
            
            // Створюємо окремий STA-потік спеціально для буфера обміну
            Thread staThread = new Thread(() =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        result = Clipboard.GetText();
                    }
                }
                catch 
                { 
                    // Ігноруємо помилки доступу, якщо інша програма заблокувала буфер
                }
            });
            
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join(); // Чекаємо завершення
            
            return Task.FromResult(result);
        }

        public Task SetTextAsync(string text)
        {
            Thread staThread = new Thread(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        Clipboard.SetText(text);
                    }
                }
                catch { }
            });
            
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            return Task.CompletedTask;
        }
    }
}