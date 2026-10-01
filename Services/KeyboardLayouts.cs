using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Installed Windows keyboard layouts: switching the foreground window's layout, and reading which characters the
    /// English and Ukrainian layouts type on each key, so the converter follows the layouts the user actually has
    /// (e.g. "Ukrainian (Enhanced)" with ґ, or "United Kingdom" instead of "US").
    /// </summary>
    public sealed partial class KeyboardLayouts : IKeyboardLayoutSwitcher
    {
        private const ushort LANG_ENGLISH = 0x09;
        private const ushort LANG_UKRAINIAN = 0x22;
        private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const int VK_SHIFT = 0x10;
        private const uint KLF_NOTELLSHELL = 0x00000080;

        // ToUnicodeEx flag (Windows 10 1607+): do not change the keyboard state, so dead keys leave no trace
        private const uint ToUnicodeKeepState = 0x4;

        // Virtual keys that type characters: digits, letters and the punctuation (OEM) keys
        private static readonly (int First, int Last)[] CharacterKeyRanges =
        {
            (0x30, 0x39), (0x41, 0x5A), (0xBA, 0xC0), (0xDB, 0xDF), (0xE2, 0xE2),
        };

        public bool SwitchForegroundWindowTo(KeyboardLanguage language)
        {
            IntPtr? layout = FindInstalled(language);
            if (layout == null) return false;

            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;

            // The window's own thread performs the switch, the same way the language bar does it
            return PostMessage(window, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout.Value);
        }

        /// <summary>
        /// A converter built from the installed English and Ukrainian layouts, or the built-in US/Ukrainian tables
        /// when one of them is missing or cannot be read.
        /// </summary>
        public static LayoutConverterService CreateConverter()
        {
            try
            {
                IntPtr? english = FindInstalled(KeyboardLanguage.English);
                IntPtr? ukrainian = FindInstalled(KeyboardLanguage.Ukrainian);
                if (english != null && ukrainian != null)
                {
                    var (englishKeys, ukrainianKeys) = LayoutTableBuilder.Build(ReadKeyPairs(english.Value, ukrainian.Value));
                    AppLog.Info($"Conversion tables built from the installed layouts ({englishKeys.Length} keys)");
                    return new LayoutConverterService(englishKeys, ukrainianKeys);
                }
                AppLog.Info("An English or Ukrainian layout is not installed; using the built-in conversion tables");
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not read the installed keyboard layouts; using the built-in conversion tables", ex);
            }
            return new LayoutConverterService();
        }

        public static IntPtr? FindInstalled(KeyboardLanguage language)
        {
            ushort primaryLanguage = language == KeyboardLanguage.English ? LANG_ENGLISH : LANG_UKRAINIAN;
            foreach (IntPtr layout in GetInstalled())
            {
                // The low word of a layout handle is its language identifier; the low 10 bits are the primary language
                if (((ulong)layout & 0x3FF) == primaryLanguage) return layout;
            }
            return null;
        }

        public static unsafe IReadOnlyList<IntPtr> GetInstalled()
        {
            int count = GetKeyboardLayoutList(0, null);
            var layouts = new IntPtr[count];
            fixed (IntPtr* buffer = layouts)
            {
                count = GetKeyboardLayoutList(count, buffer);
            }
            return layouts.AsSpan(0, count).ToArray();
        }

        /// <summary>
        /// Loads a layout by its identifier (e.g. "00020422" for Ukrainian (Enhanced)) without activating it; for the
        /// smoke test, which checks the conversion tables against the real layout files. Returns 0 on failure.
        /// </summary>
        internal static IntPtr Load(string layoutId) => LoadKeyboardLayout(layoutId, KLF_NOTELLSHELL);

        internal static void Unload(IntPtr layout) => UnloadKeyboardLayout(layout);

        /// <summary>Characters typed by the same key with the same Shift state in both layouts.</summary>
        public static List<(char English, char Ukrainian)> ReadKeyPairs(IntPtr english, IntPtr ukrainian)
        {
            var pairs = new List<(char, char)>();
            var keyState = new byte[256];
            foreach (bool shift in new[] { false, true })
            {
                keyState[VK_SHIFT] = shift ? (byte)0x80 : (byte)0;
                foreach (var (first, last) in CharacterKeyRanges)
                {
                    for (int virtualKey = first; virtualKey <= last; virtualKey++)
                    {
                        char? en = TypedCharacter(virtualKey, keyState, english);
                        char? ua = TypedCharacter(virtualKey, keyState, ukrainian);
                        if (en != null && ua != null) pairs.Add((en.Value, ua.Value));
                    }
                }
            }
            return pairs;
        }

        private static unsafe char? TypedCharacter(int virtualKey, byte[] keyState, IntPtr layout)
        {
            uint scanCode = MapVirtualKeyEx((uint)virtualKey, MAPVK_VK_TO_VSC, layout);
            if (scanCode == 0) return null;

            char* buffer = stackalloc char[8];
            int length;
            fixed (byte* state = keyState)
            {
                length = ToUnicodeEx((uint)virtualKey, scanCode, state, buffer, 8, ToUnicodeKeepState, layout);
            }

            // Dead keys (-1), no character (0) and ligatures (>1) are skipped
            return length == 1 && !char.IsControl(buffer[0]) ? buffer[0] : null;
        }

        [LibraryImport("user32.dll")]
        private static unsafe partial int GetKeyboardLayoutList(int nBuff, IntPtr* lpList);

        [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
        private static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [LibraryImport("user32.dll")]
        private static unsafe partial int ToUnicodeEx(
            uint wVirtKey, uint wScanCode, byte* lpKeyState, char* pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        [LibraryImport("user32.dll", EntryPoint = "LoadKeyboardLayoutW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnloadKeyboardLayout(IntPtr hkl);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
