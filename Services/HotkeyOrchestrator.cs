using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace MovaCore.Services
{
    public class HotkeyOrchestrator
    {
        // Longer text is not re-selected: one Shift+Left per character would take noticeable time
        private const int MaxReselectLength = 300;

        private readonly IHotkeyService _hotkeyService;
        private readonly ILayoutConverterService _converterService;
        private readonly IClipboardService _clipboardService;
        private readonly IKeyboardLayoutSwitcher _layoutSwitcher;
        private readonly ClipboardGate _clipboardGate;

        private volatile bool _restoreClipboard = true;
        private volatile bool _switchLayout = true;
        private volatile bool _selectConvertedText = true;
        private volatile bool _convertLastWord;

        public event EventHandler<string>? ConversionFailed;

        /// <summary>Raised when the hotkey found nothing selected (and no word before the caret), so nothing changed.</summary>
        public event EventHandler? NothingSelected;

        /// <summary>Put the user's previous clipboard content back after converting (a user setting).</summary>
        public bool RestoreClipboard
        {
            get => _restoreClipboard;
            set => _restoreClipboard = value;
        }

        /// <summary>Switch the target window to the layout of the converted text (a user setting).</summary>
        public bool SwitchLayout
        {
            get => _switchLayout;
            set => _switchLayout = value;
        }

        /// <summary>Select the pasted text again, so a second press converts it back (a user setting).</summary>
        public bool SelectConvertedText
        {
            get => _selectConvertedText;
            set => _selectConvertedText = value;
        }

        /// <summary>With nothing selected, convert the word before the caret (a user setting, off by default).</summary>
        public bool ConvertLastWord
        {
            get => _convertLastWord;
            set => _convertLastWord = value;
        }

        /// <summary>How long to wait for the foreground app to put the selection on the clipboard.</summary>
        internal TimeSpan CopyTimeout { get; init; } = TimeSpan.FromSeconds(1);

        /// <summary>How long to wait for the foreground app to read the converted text after Ctrl+V.</summary>
        internal TimeSpan PasteTimeout { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>Pause after the paste is read, for apps that read the clipboard twice or insert slowly.</summary>
        internal TimeSpan RestoreDelay { get; init; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Pause after Ctrl+V when the converted text was read before it (see <see cref="TextPaster"/>).</summary>
        internal TimeSpan UnobservedPasteDelay { get; init; } = TimeSpan.FromMilliseconds(600);

        public HotkeyOrchestrator(
            IHotkeyService hotkeyService,
            ILayoutConverterService converterService,
            IClipboardService clipboardService,
            IKeyboardLayoutSwitcher layoutSwitcher,
            ClipboardGate? clipboardGate = null)
        {
            _hotkeyService = hotkeyService;
            _converterService = converterService;
            _clipboardService = clipboardService;
            _layoutSwitcher = layoutSwitcher;
            _clipboardGate = clipboardGate ?? new ClipboardGate();
        }

        public async Task ExecuteConversionAsync()
        {
            // Every hotkey press starts on its own thread-pool thread; a press during a conversion (or a dictation
            // paste) is dropped
            if (!_clipboardGate.TryEnter()) return;

            try
            {
                // 0. Our Ctrl+C is about to replace whatever the user has on the clipboard, so keep a copy
                ClipboardSnapshot? snapshot = RestoreClipboard ? await _clipboardService.TryCaptureAsync() : null;

                // 1. Copy the selection. Instead of clearing the clipboard first, watch its sequence number:
                //    if it does not change, nothing was selected, and stale clipboard content is never pasted.
                if (!await CopyAsync())
                {
                    // Off by default: if the first copy was merely slow, this would extend the user's selection
                    if (!ConvertLastWord || !await CopyLastWordAsync())
                    {
                        NothingSelected?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
                uint sequenceAfterCopy = _clipboardService.GetSequenceNumber();

                // 2. Read (null: the copied content is not text, e.g. an image or files) and convert
                string? capturedText = await _clipboardService.TryGetTextAsync();
                string? converted = string.IsNullOrWhiteSpace(capturedText) ? null : _converterService.Convert(capturedText);
                if (capturedText == null || converted == null || converted == capturedText)
                {
                    await RestoreAsync(snapshot, sequenceAfterCopy);
                    return;
                }

                // 3. Paste the converted text over the selection
                long setStarted = Stopwatch.GetTimestamp();
                if (!await _clipboardService.TrySetTextAsync(converted))
                {
                    AppLog.Error("Could not put the converted text on the clipboard");
                    ConversionFailed?.Invoke(this, Strings.BalloonClipboardWriteFailed);
                    await RestoreAsync(snapshot, sequenceAfterCopy);
                    return;
                }

                await Task.Delay(50);
                // Delayed rendering reports only the first read: once something (a clipboard manager) has read the
                // text, the paste itself cannot be seen
                bool readEarly = await _clipboardService.WaitForTextReadAsync(setStarted, TimeSpan.Zero);
                long pasteStarted = Stopwatch.GetTimestamp();
                _hotkeyService.SimulatePaste();

                if (snapshot == null && !SelectConvertedText && !SwitchLayout) return;

                // 4. Everything else waits until the app has actually read the converted text. Restoring earlier would
                //    make it paste the old clipboard content instead; if it never reads it, the converted text stays.
                if (readEarly)
                {
                    await Task.Delay(UnobservedPasteDelay);
                }
                else if (!await _clipboardService.WaitForTextReadAsync(pasteStarted, PasteTimeout))
                {
                    AppLog.Info("The converted text was not pasted in time; the clipboard keeps it");
                    return;
                }
                else
                {
                    await Task.Delay(RestoreDelay);
                }

                if (SelectConvertedText && TryCountCaretSteps(converted, out int steps))
                    _hotkeyService.SimulateSelectLeft(steps);

                if (SwitchLayout)
                    _layoutSwitcher.SwitchForegroundWindowTo(_converterService.TargetOf(capturedText));

                await RestoreAsync(snapshot, sequenceAfterCopy);
            }
            catch (Exception ex)
            {
                // Never log the text itself: it is the user's clipboard content
                AppLog.Error("Conversion failed", ex);
                ConversionFailed?.Invoke(this, Strings.BalloonUnexpectedError(ex.Message));
            }
            finally
            {
                _clipboardGate.Exit();
            }
        }

        /// <summary>
        /// How many Shift+Left presses select <paramref name="text"/> right after pasting it. Only for single-line text
        /// of reasonable length: editors count line breaks differently, and a caret step is a text element (an emoji is
        /// one step but two UTF-16 units).
        /// </summary>
        internal static bool TryCountCaretSteps(string text, out int steps)
        {
            steps = 0;
            if (text.Contains('\n') || text.Contains('\r')) return false;

            steps = new StringInfo(text).LengthInTextElements;
            return steps > 0 && steps <= MaxReselectLength;
        }

        private async Task<bool> CopyLastWordAsync()
        {
            _hotkeyService.SimulateSelectWordLeft();
            return await CopyAsync();
        }

        private async Task<bool> CopyAsync()
        {
            uint sequenceBefore = _clipboardService.GetSequenceNumber();
            _hotkeyService.SimulateCopy();
            return await WaitForClipboardChangeAsync(sequenceBefore);
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
