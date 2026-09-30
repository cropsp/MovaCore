using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LayoutConverter.App.Services
{
    public class HotkeyOrchestrator
    {
        private readonly IHotkeyService _hotkeyService;
        private readonly ILayoutConverterService _converterService;
        private readonly IClipboardService _clipboardService;

        // 0 = idle, 1 = busy. Interlocked because every hotkey press starts on its own thread-pool thread.
        private int _isProcessing;

        public event EventHandler<string>? ConversionCompleted;

        /// <summary>How long to wait for the foreground app to put the selection on the clipboard.</summary>
        internal TimeSpan CopyTimeout { get; init; } = TimeSpan.FromSeconds(1);

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
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0) return;

            try
            {
                // 1. Copy the selection. Instead of clearing the clipboard first, watch its sequence number:
                //    if it does not change, nothing was selected, and stale clipboard content is never pasted.
                uint sequenceBefore = _clipboardService.GetSequenceNumber();
                _hotkeyService.SimulateCopy();

                if (!await WaitForClipboardChangeAsync(sequenceBefore)) return;

                // 2. Read (null: the copied content is not text, e.g. an image or files)
                string? capturedText = await _clipboardService.TryGetTextAsync();
                if (string.IsNullOrWhiteSpace(capturedText)) return;

                // 3. Convert and paste back
                string converted = await _converterService.ConvertAsync(capturedText);
                if (converted == capturedText) return;

                if (!await _clipboardService.TrySetTextAsync(converted))
                {
                    AppLog.Error("Could not put the converted text on the clipboard");
                    ConversionCompleted?.Invoke(this, "Could not put the converted text on the clipboard. Please try again.");
                    return;
                }

                await Task.Delay(50);
                _hotkeyService.SimulatePaste();
            }
            catch (Exception ex)
            {
                // Never log the text itself: it is the user's clipboard content
                AppLog.Error("Conversion failed", ex);
                ConversionCompleted?.Invoke(this, $"System Error: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _isProcessing, 0);
            }
        }

        private async Task<bool> WaitForClipboardChangeAsync(uint sequenceBefore)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < CopyTimeout)
            {
                await Task.Delay(20);
                if (_clipboardService.GetSequenceNumber() != sequenceBefore) return true;
            }
            return false;
        }
    }
}
