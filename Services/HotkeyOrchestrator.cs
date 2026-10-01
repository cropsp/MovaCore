using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MovaCore.Services
{
    public class HotkeyOrchestrator
    {
        private readonly IHotkeyService _hotkeyService;
        private readonly ILayoutConverterService _converterService;
        private readonly IClipboardService _clipboardService;

        // 0 = idle, 1 = busy. Interlocked because every hotkey press starts on its own thread-pool thread.
        private int _isProcessing;
        private volatile bool _restoreClipboard = true;

        public event EventHandler<string>? ConversionFailed;

        /// <summary>Put the user's previous clipboard content back after converting (a user setting).</summary>
        public bool RestoreClipboard
        {
            get => _restoreClipboard;
            set => _restoreClipboard = value;
        }

        /// <summary>How long to wait for the foreground app to put the selection on the clipboard.</summary>
        internal TimeSpan CopyTimeout { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>How long to wait for the foreground app to read the converted text after Ctrl+V.</summary>
        internal TimeSpan PasteTimeout { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>Pause between the paste being read and restoring the clipboard, for apps that read it twice.</summary>
        internal TimeSpan RestoreDelay { get; init; } = TimeSpan.FromMilliseconds(250);

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
                // 0. Our Ctrl+C is about to replace whatever the user has on the clipboard, so keep a copy
                ClipboardSnapshot? snapshot = RestoreClipboard ? await _clipboardService.TryCaptureAsync() : null;

                // 1. Copy the selection. Instead of clearing the clipboard first, watch its sequence number:
                //    if it does not change, nothing was selected, and stale clipboard content is never pasted.
                uint sequenceBefore = _clipboardService.GetSequenceNumber();
                _hotkeyService.SimulateCopy();

                if (!await WaitForClipboardChangeAsync(sequenceBefore)) return;
                uint sequenceAfterCopy = _clipboardService.GetSequenceNumber();

                // 2. Read (null: the copied content is not text, e.g. an image or files) and convert
                string? capturedText = await _clipboardService.TryGetTextAsync();
                string? converted = string.IsNullOrWhiteSpace(capturedText) ? null : _converterService.Convert(capturedText);
                if (converted == null || converted == capturedText)
                {
                    await RestoreAsync(snapshot, sequenceAfterCopy);
                    return;
                }

                // 3. Paste the converted text over the selection
                if (!await _clipboardService.TrySetTextAsync(converted))
                {
                    AppLog.Error("Could not put the converted text on the clipboard");
                    ConversionFailed?.Invoke(this, "Could not put the converted text on the clipboard. Please try again.");
                    await RestoreAsync(snapshot, sequenceAfterCopy);
                    return;
                }

                await Task.Delay(50);
                long pasteStarted = Stopwatch.GetTimestamp();
                _hotkeyService.SimulatePaste();

                // 4. Restore only once the app has actually read the converted text. Restoring earlier would make it
                //    paste the old clipboard content instead; if it never reads it, the converted text stays.
                if (snapshot != null)
                {
                    if (await _clipboardService.WaitForTextReadAsync(pasteStarted, PasteTimeout))
                    {
                        await Task.Delay(RestoreDelay);
                        await RestoreAsync(snapshot, sequenceAfterCopy);
                    }
                    else
                    {
                        AppLog.Info("The converted text was not pasted in time; the clipboard keeps it");
                    }
                }
            }
            catch (Exception ex)
            {
                // Never log the text itself: it is the user's clipboard content
                AppLog.Error("Conversion failed", ex);
                ConversionFailed?.Invoke(this, $"System Error: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _isProcessing, 0);
            }
        }

        private async Task RestoreAsync(ClipboardSnapshot? snapshot, uint sequenceAfterCopy)
        {
            if (snapshot != null) await _clipboardService.TryRestoreAsync(snapshot, sequenceAfterCopy);
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
