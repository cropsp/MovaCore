using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MovaCore.Services
{
    /// <summary>
    /// Pastes text into the focused application through the clipboard and puts the user's clipboard back afterwards:
    /// the paste half of a conversion, without the copy.
    /// </summary>
    public sealed class TextPaster
    {
        private readonly IHotkeyService _hotkeyService;
        private readonly IClipboardService _clipboardService;
        private readonly ClipboardGate _clipboardGate;

        public TextPaster(IHotkeyService hotkeyService, IClipboardService clipboardService, ClipboardGate clipboardGate)
        {
            _hotkeyService = hotkeyService;
            _clipboardService = clipboardService;
            _clipboardGate = clipboardGate;
        }

        /// <summary>A conversion holds the gate for at most a few seconds; dictated text must not be dropped meanwhile.</summary>
        internal TimeSpan GateTimeout { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>How long to wait for the foreground app to read the text after Ctrl+V.</summary>
        internal TimeSpan PasteTimeout { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>Pause after the paste is read, for apps that read the clipboard twice or insert slowly.</summary>
        internal TimeSpan RestoreDelay { get; init; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Blocks on simulated key presses: call it on a worker thread, never the UI or hook thread.</summary>
        public async Task<PasteResult> PasteAsync(string text, bool restoreClipboard)
        {
            if (!await _clipboardGate.WaitAsync(GateTimeout)) return PasteResult.Busy;

            try
            {
                ClipboardSnapshot? snapshot = restoreClipboard ? await _clipboardService.TryCaptureAsync() : null;

                if (!await _clipboardService.TrySetTextAsync(text)) return PasteResult.ClipboardFailed;
                uint sequenceAfterSet = _clipboardService.GetSequenceNumber();

                await Task.Delay(50);
                long pasteStarted = Stopwatch.GetTimestamp();
                _hotkeyService.SimulatePaste();

                // Restoring before the app has read the text would make it paste the old clipboard content instead
                if (!await _clipboardService.WaitForTextReadAsync(pasteStarted, PasteTimeout))
                    return PasteResult.NotObserved;

                if (snapshot != null)
                {
                    await Task.Delay(RestoreDelay);
                    await _clipboardService.TryRestoreAsync(snapshot, sequenceAfterSet);
                }
                return PasteResult.Pasted;
            }
            finally
            {
                _clipboardGate.Exit();
            }
        }
    }
}
