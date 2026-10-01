using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MovaCore.Services
{
    /// <summary>
    /// Native Win32 implementation of Clipboard Service.
    /// Perfectly compatible with Native AOT and extremely stable.
    /// </summary>
    public partial class ClipboardService : IClipboardService
    {
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool OpenClipboard(IntPtr hWndNewOwner);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseClipboard();

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EmptyClipboard();

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial IntPtr GetClipboardData(uint uFormat);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [LibraryImport("user32.dll")]
        private static partial uint GetClipboardSequenceNumber();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GlobalLock(IntPtr hMem);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GlobalUnlock(IntPtr hMem);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr GlobalFree(IntPtr hMem);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial UIntPtr GlobalSize(IntPtr hMem);

        const uint CF_UNICODETEXT = 13;
        const uint GMEM_MOVEABLE = 0x0002;

        // About 1 second, to wait for another application to release the clipboard
        const int OpenAttempts = 20;
        const int OpenRetryDelayMs = 50;

        public uint GetSequenceNumber() => GetClipboardSequenceNumber();

        public Task<string?> TryGetTextAsync()
        {
            return Task.Run<string?>(() =>
            {
                if (!TryOpenClipboard()) return null;

                try
                {
                    IntPtr hGlobal = GetClipboardData(CF_UNICODETEXT);
                    if (hGlobal == IntPtr.Zero) return null; // the clipboard holds no text (an image, files, etc.)

                    IntPtr pGlobal = GlobalLock(hGlobal);
                    if (pGlobal == IntPtr.Zero) return null;

                    try
                    {
                        // Do not rely on the trailing '\0': read no more than the block size
                        int maxChars = (int)((ulong)GlobalSize(hGlobal) / sizeof(char));
                        string text = Marshal.PtrToStringUni(pGlobal, maxChars);
                        int terminator = text.IndexOf('\0');
                        return terminator >= 0 ? text.Substring(0, terminator) : text;
                    }
                    finally
                    {
                        GlobalUnlock(hGlobal);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            });
        }

        public Task<bool> TrySetTextAsync(string text)
        {
            return Task.Run(() =>
            {
                if (!TryOpenClipboard()) return false;

                try
                {
                    if (!EmptyClipboard()) return false;

                    byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
                    IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                    if (hGlobal == IntPtr.Zero) return false;

                    IntPtr pGlobal = GlobalLock(hGlobal);
                    if (pGlobal == IntPtr.Zero)
                    {
                        GlobalFree(hGlobal);
                        return false;
                    }

                    Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
                    GlobalUnlock(hGlobal);

                    // After a successful SetClipboardData the system owns the memory; otherwise we free it ourselves
                    if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                    {
                        GlobalFree(hGlobal);
                        return false;
                    }

                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            });
        }

        private static bool TryOpenClipboard()
        {
            // The clipboard may be held briefly by another application (Telegram, browsers, clipboard managers)
            for (int i = 0; i < OpenAttempts; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(OpenRetryDelayMs);
            }
            return false;
        }
    }
}
