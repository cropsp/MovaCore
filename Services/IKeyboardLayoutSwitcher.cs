using MovaCore.Models;

namespace MovaCore.Services
{
    public interface IKeyboardLayoutSwitcher
    {
        /// <summary>
        /// Asks the foreground window to switch to an installed layout of <paramref name="language"/>; does nothing if
        /// none is installed. Returns whether a request was sent.
        /// </summary>
        bool SwitchForegroundWindowTo(KeyboardLanguage language);
    }
}
