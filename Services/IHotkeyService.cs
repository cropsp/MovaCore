using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;

namespace MovaCore.Services
{
    public interface IHotkeyService : IDisposable
    {
        /// <summary>Starts the global keyboard hook. Can be called again after <see cref="Stop"/>.</summary>
        void Start();

        /// <summary>Stops the hook without disposing it.</summary>
        void Stop();

        void SetTrigger(Hotkey trigger);

        /// <summary>The hold-to-talk dictation hotkey, or null while dictation is off.</summary>
        void SetSpeechHotkey(Hotkey? hotkey);

        /// <summary>Applications (process names) in which the trigger is left alone.</summary>
        void SetExcludedProcesses(IEnumerable<string> processNames);

        CopyPasteKeys CopyPasteKeys { get; set; }

        /// <summary>Raised on the hook thread when the trigger key is released.</summary>
        event EventHandler? HotkeyTriggered;

        /// <summary>Raised on the hook thread on the first press of the speech hotkey (not on auto-repeats).</summary>
        event EventHandler? SpeechHotkeyPressed;

        /// <summary>Raised on the hook thread when the speech hotkey is released.</summary>
        event EventHandler? SpeechHotkeyReleased;

        /// <summary>Raised on a worker thread when the hook cannot start or stops with an error.</summary>
        event EventHandler<Exception>? HookFailed;

        /// <summary>
        /// Captures the next key the user presses together with the held modifiers, without passing it to any
        /// application and without triggering a conversion. Esc or cancellation returns null.
        /// </summary>
        Task<Hotkey?> CaptureHotkeyAsync(CancellationToken cancellationToken);

        void SimulateCopy();
        void SimulatePaste();

        /// <summary>Shift+Left the given number of times: selects text just pasted before the caret.</summary>
        void SimulateSelectLeft(int caretSteps);

        /// <summary>Ctrl+Shift+Left: selects the word before the caret.</summary>
        void SimulateSelectWordLeft();
    }
}
