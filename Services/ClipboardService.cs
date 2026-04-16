using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LayoutConverter.App.Services
{
    public interface IClipboardService
    {
        Task<string> GetTextAsync();
        Task SetTextAsync(string text);
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

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern UIntPtr GlobalSize(IntPtr hMem);

        const uint CF_UNICODETEXT = 13;
        const uint GMEM_MOVEABLE = 0x0002;

        public Task<string> GetTextAsync()
        {
            return Task.Run(() =>
            {
                string result = string.Empty;

                // Робимо до 20 спроб (близько 1 секунди), щоб пробити замок
                for (int i = 0; i < 20; i++)
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        try
                        {
                            IntPtr hGlobal = GetClipboardData(CF_UNICODETEXT);
                            if (hGlobal != IntPtr.Zero)
                            {
                                IntPtr pGlobal = GlobalLock(hGlobal);
                                if (pGlobal != IntPtr.Zero)
                                {
                                    try
                                    {
                                        result = Marshal.PtrToStringUni(pGlobal);
                                    }
                                    finally
                                    {
                                        GlobalUnlock(hGlobal);
                                    }
                                }
                            }
                            
                            // Якщо ми отримали текст, виходимо
                            if (!string.IsNullOrEmpty(result)) break;
                        }
                        finally
                        {
                            CloseClipboard();
                        }
                    }
                    
                    // Якщо буфер зайнятий - чекаємо 50мс
                    Thread.Sleep(50);
                }

                return result ?? string.Empty;
            });
        }

        public Task SetTextAsync(string text)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(text)) text = string.Empty;

                for (int i = 0; i < 10; i++)
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        try
                        {
                            EmptyClipboard();

                            if (text.Length > 0)
                            {
                                byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
                                IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                                if (hGlobal != IntPtr.Zero)
                                {
                                    IntPtr pGlobal = GlobalLock(hGlobal);
                                    if (pGlobal != IntPtr.Zero)
                                    {
                                        Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
                                        GlobalUnlock(hGlobal);
                                        if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                                        {
                                            // В разі помилки Marshal.FreeHGlobal не потрібен для GMEM_MOVEABLE
                                        }
                                    }
                                }
                            }
                            break; // Успіх
                        }
                        finally
                        {
                            CloseClipboard();
                        }
                    }
                    Thread.Sleep(50);
                }
            });
        }
    }
}