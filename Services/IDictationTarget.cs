using System;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>What dictated text is pasted into, as far as MovaCore can tell, and when the user may have changed it.</summary>
    public interface IDictationTarget
    {
        /// <summary>The window and control with the keyboard focus now, or null if that cannot be told.</summary>
        DictationFocus? GetFocus();

        /// <summary>Raised on any thread when the user may have moved the caret or typed: a key press or a click.</summary>
        event EventHandler? Interrupted;

        /// <summary>Clicks are watched (and reported by <see cref="Interrupted"/>) only while this is on.</summary>
        void WatchClicks(bool watch);
    }
}
