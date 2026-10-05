using System;
using System.Diagnostics;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Remembers the last dictated text and where it went, so that the next phrase can continue it (a space, a lower
    /// case first letter). Windows cannot tell what precedes the caret in every application, so this only knows the
    /// text MovaCore pasted itself: any key press, click or change of focus since then makes it unknown.
    /// </summary>
    public sealed class DictationContext : IDisposable
    {
        private const int KeptLength = 64; // enough to see how the text ends

        private readonly IDictationTarget _target;
        private readonly object _lock = new();
        private string? _text; // guarded by _lock
        private DictationFocus? _focus; // guarded by _lock
        private long _rememberedAt; // guarded by _lock

        public DictationContext(IDictationTarget target)
        {
            _target = target;
            _target.Interrupted += OnInterrupted;
        }

        /// <summary>After this long without a dictation, the text before the caret is no longer assumed.</summary>
        internal TimeSpan MaxAge { get; init; } = TimeSpan.FromMinutes(10);

        /// <summary>The text just before the caret, if it is the last dictation and the focus is still there; else null.</summary>
        public string? TextBefore()
        {
            DictationFocus? focus = _target.GetFocus();
            lock (_lock)
            {
                if (_text == null) return null;
                if (focus == null || focus != _focus || Stopwatch.GetElapsedTime(_rememberedAt) > MaxAge)
                {
                    ForgetLocked();
                    return null;
                }
                return _text;
            }
        }

        /// <summary>Called right after <paramref name="text"/> was pasted.</summary>
        public void Remember(string text)
        {
            DictationFocus? focus = _target.GetFocus();
            lock (_lock)
            {
                if (focus == null)
                {
                    ForgetLocked();
                    return;
                }
                bool watching = _text != null;
                _text = text.Length > KeptLength ? text[^KeptLength..] : text;
                _focus = focus;
                _rememberedAt = Stopwatch.GetTimestamp();
                // Under the lock, so that the target sees watch and unwatch in the order they happened
                if (!watching) _target.WatchClicks(true);
            }
        }

        public void Forget()
        {
            lock (_lock) ForgetLocked();
        }

        private void ForgetLocked()
        {
            if (_text == null) return;
            _text = null;
            _focus = null;
            _target.WatchClicks(false);
        }

        private void OnInterrupted(object? sender, EventArgs e) => Forget();

        public void Dispose() => _target.Interrupted -= OnInterrupted;
    }
}
