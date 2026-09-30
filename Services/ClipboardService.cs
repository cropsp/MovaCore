using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LayoutConverter.App.Services
{
    public interface IClipboardService
    {
        /// <summary>
        /// Changes whenever any application writes to or empties the clipboard. Does not open the clipboard.
        /// </summary>
        uint GetSequenceNumber();

        /// <summary>
        /// Returns the clipboard text, or null if the clipboard holds no text or stays locked by another application.
        /// </summary>
        Task<string?> TryGetTextAsync();

        /// <summary>
        /// Returns false if the clipboard stays locked by another application or Windows rejects the data.
        /// </summary>
        Task<bool> TrySetTextAsync(string text);
    }

    /// <summary>
    /// Native Win32 implementation of Clipboard Service.
    /// Perfectly compatible with Native AOT and extremely stable.
    /// </summary>
    public class ClipboardService : IClipboardService
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll")]
        static extern uint GetClipboardSequenceNumber();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern UIntPtr GlobalSize(IntPtr hMem);

        const uint CF_UNICODETEXT = 13;
        const uint GMEM_MOVEABLE = 0x0002;

        // Близько 1 секунди, щоб дочекатися, поки інша програма відпустить буфер
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
                    if (hGlobal == IntPtr.Zero) return null; // у буфері не текст (зображення, файли тощо)

                    IntPtr pGlobal = GlobalLock(hGlobal);
                    if (pGlobal == IntPtr.Zero) return null;

                    try
                    {
                        // Не покладаємося на завершальний '\0': читаємо не більше за розмір блоку
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

                    // Після успішного SetClipboardData пам'ять належить системі, інакше звільняємо її самі
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
            // Буфер може бути ненадовго зайнятий іншою програмою (Telegram, браузери, менеджери буфера)
            for (int i = 0; i < OpenAttempts; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                Thread.Sleep(OpenRetryDelayMs);
            }
            return false;
        }
    }
}
