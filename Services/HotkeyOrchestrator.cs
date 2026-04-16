using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LayoutConverter.App.Services;

namespace LayoutConverter.App.Services
{
    public class HotkeyOrchestrator
    {
        private readonly IHotkeyService _hotkeyService;
        private readonly ILayoutConverterService _converterService;
        private readonly IClipboardService _clipboardService;
        
        private bool _isProcessing = false;

        public event EventHandler<string> ConversionCompleted;

        public HotkeyOrchestrator(
            IHotkeyService hotkeyService,
            ILayoutConverterService converterService,
            IClipboardService clipboardService)
        {
            _hotkeyService = hotkeyService;
            _converterService = converterService;
            _clipboardService = clipboardService;
        }

        public async Task ExecuteConversionAsync()
        {
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                // 1. Clear
                await _clipboardService.SetTextAsync("");
                await Task.Delay(50); 

                // 2. Simulate Copy
                _hotkeyService.SimulateCopy();

                // 3. Polling
                string capturedText = "";
                Stopwatch sw = Stopwatch.StartNew();
                
                while (sw.ElapsedMilliseconds < 1000) 
                {
                    await Task.Delay(50); 
                    capturedText = await _clipboardService.GetTextAsync();
                    
                    if (!string.IsNullOrWhiteSpace(capturedText))
                    {
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(capturedText))
                {
                    string converted = await _converterService.ConvertAsync(capturedText);
                    
                    if (converted != capturedText)
                    {
                        await _clipboardService.SetTextAsync(converted);
                        await Task.Delay(50); 
                        _hotkeyService.SimulatePaste();
                    }
                }
            }
            catch (Exception ex)
            {
                // Keep errors for troubleshooting
                ConversionCompleted?.Invoke(this, $"System Error: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }
    }
}
