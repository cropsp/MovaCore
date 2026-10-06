using System;
using MovaCore.Models;
using SharpHook.Data;

namespace MovaCore.Services
{
    /// <summary>
    /// Hook-independent press/release logic for the conversion trigger and the speech hotkey. Not thread-safe: every
    /// call comes from the keyboard hook thread.
    /// </summary>
    public sealed class HotkeyStateTracker
    {
        private Hotkey? _held; // the hotkey whose press was swallowed and whose release is still to come
        private bool _heldIsSpeech;

        /// <summary>The hotkey currently held down, if its press was ours.</summary>
        public Hotkey? Held => _held;

        /// <param name="isExcluded">Whether the foreground application keeps its own shortcut. Called only for a
        /// first press that matches a hotkey, since it looks up the foreground process.</param>
        public HotkeyAction OnKeyPressed(
            KeyCode key, EventMask mask, bool simulated, Hotkey trigger, Hotkey? speech, Func<bool> isExcluded)
        {
            // Our own simulated shortcuts never count as a hotkey
            if (simulated) return HotkeyAction.PassThrough;

            if (_held is { } held)
            {
                // Windows repeats key-down while a key is held. Repeats stay ours even if a modifier was released
                // in the meantime; any other key belongs to the application.
                return key == held.Key ? HotkeyAction.Suppress : HotkeyAction.PassThrough;
            }

            bool isSpeech;
            Hotkey matched;
            if (HotkeyMatching.Matches(trigger, key, mask))
            {
                (matched, isSpeech) = (trigger, false);
            }
            else if (speech is { } s && s != trigger && HotkeyMatching.Matches(s, key, mask))
            {
                (matched, isSpeech) = (s, true);
            }
            else
            {
                return HotkeyAction.PassThrough;
            }

            if (isExcluded()) return HotkeyAction.PassThrough;

            _held = matched;
            _heldIsSpeech = isSpeech;
            return isSpeech ? HotkeyAction.SpeechPressed : HotkeyAction.TriggerPressed;
        }

        /// <summary>
        /// Only a release whose press was swallowed is ours. Modifiers are not checked: users often let go of them
        /// before the key itself.
        /// </summary>
        public HotkeyAction OnKeyReleased(KeyCode key)
        {
            if (_held is not { } held || key != held.Key) return HotkeyAction.PassThrough;

            _held = null;
            return _heldIsSpeech ? HotkeyAction.SpeechReleased : HotkeyAction.TriggerReleased;
        }

        /// <summary>Forgets a held hotkey, e.g. when the hook restarts and the release may never have been seen.</summary>
        public void Reset()
        {
            _held = null;
        }
    }
}
