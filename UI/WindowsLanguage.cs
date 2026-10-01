using System.Runtime.InteropServices;
using MovaCore.Models;

namespace MovaCore.UI
{
    /// <summary>
    /// Picks the interface language for the "Automatic" setting from Windows. CultureInfo cannot be used for this: the
    /// app runs with InvariantGlobalization, so every culture is the invariant one.
    /// </summary>
    internal static partial class WindowsLanguage
    {
        private const int PrimaryLanguageMask = 0x3FF;
        private const int LANG_UKRAINIAN = 0x22;

        /// <summary>The setting itself, except for <see cref="UiLanguage.Auto"/>: Ukrainian if Windows is, else English.</summary>
        public static UiLanguage Resolve(UiLanguage setting)
        {
            if (setting != UiLanguage.Auto) return setting;

            int primaryLanguage = GetUserDefaultUILanguage() & PrimaryLanguageMask;
            return primaryLanguage == LANG_UKRAINIAN ? UiLanguage.Ukrainian : UiLanguage.English;
        }

        [LibraryImport("kernel32.dll")]
        private static partial ushort GetUserDefaultUILanguage();
    }
}
