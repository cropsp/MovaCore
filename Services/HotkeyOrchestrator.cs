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

        public HotkeyOrchestrator(
            IHotkeyService hotkeyService,
            ILayoutConverterService converterService,
            IClipboardService clipboardService)
        {
            _hotkeyService = hotkeyService;
            _converterService = converterService;
            _clipboardService = clipboardService;
        }

        public async void ExecuteConversionAsync()
        {
            try
            {
                // 1. Snapshot current clipboard content
                string snapshot = await _clipboardService.GetTextAsync();

                // 2. Simulate Ctrl + C to copy selected text
                _hotkeyService.SimulateCopy();

                // 3. Smart Clipboard Polling
                // Loop for max 300ms, checking every 30ms for content change
                string newContent = snapshot;
                Stopwatch sw = Stopwatch.StartNew();
                
                while (sw.ElapsedMilliseconds < 300)
                {
                    await Task.Delay(30);
                    newContent = await _clipboardService.GetTextAsync();
                    
                    if (newContent != snapshot)
                    {
                        break; // Content changed!
                    }
                }

                // 4. Proceed with conversion if we have text (even if it didn't change, we try)
                if (!string.IsNullOrEmpty(newContent))
                {
                    // 5. Convert and update clipboard
                    string converted = await _converterService.ConvertAsync(newContent);
                    
                    if (converted != newContent)
                    {
                        await _clipboardService.SetTextAsync(converted);
                        
                        // Wait a tiny bit for the OS to accept the new clipboard
                        await Task.Delay(50);
                        
                        // 6. Simulate Ctrl + V to paste the converted text
                        _hotkeyService.SimulatePaste();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Conversion error: {ex.Message}");
            }
        }
    }
}
