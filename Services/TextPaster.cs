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

        /// <summary>
        /// Pause after Ctrl+V when the paste cannot be seen: something (a clipboard manager, a browser) read the text
        /// as soon as it was put there, and later reads no longer reach us.
        /// </summary>
        internal TimeSpan UnobservedPasteDelay { get; init; } = TimeSpan.FromMilliseconds(600);

        /// <summary>Blocks on simulated key presses: call it on a worker thread, never the UI or hook thread.</summary>
        public async Task<PasteResult> PasteAsync(string text, bool restoreClipboard)
        {
            if (!await _clipboardGate.WaitAsync(GateTimeout)) return PasteResult.Busy;

            try
            {
                ClipboardSnapshot? snapshot = restoreClipboard ? await _clipboardService.TryCaptureAsync() : null;

                long setStarted = Stopwatch.GetTimestamp();
                if (!await _clipboardService.TrySetTextAsync(text)) return PasteResult.ClipboardFailed;
                uint sequenceAfterSet = _clipboardService.GetSequenceNumber();

                await Task.Delay(50);
                // Delayed rendering reports only the first read: if something has read the text already, the paste
                // itself will not be seen, so wait a fixed time instead of the read
                bool readEarly = await _clipboardService.WaitForTextReadAsync(setStarted, TimeSpan.Zero);
                long pasteStarted = Stopwatch.GetTimestamp();
                _hotkeyService.SimulatePaste();

                if (readEarly)
                {
                    await Task.Delay(UnobservedPasteDelay);
                }
                else if (!await _clipboardService.WaitForTextReadAsync(pasteStarted, PasteTimeout))
                {
                    // Restoring before the app has read the text would make it paste the old clipboard content instead
                    return PasteResult.NotObserved;
                }
                else
                {
                    await Task.Delay(RestoreDelay);
                }

                if (snapshot != null) await _clipboardService.TryRestoreAsync(snapshot, sequenceAfterSet);
                return PasteResult.Pasted;
            }
            finally
            {
                _clipboardGate.Exit();
            }
        }
    }
}
