using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MovaCore.Services
{
    /// <summary>
    /// Win32 clipboard access through P/Invoke (no COM/OLE, works under Native AOT).
    /// A hidden message-only window owns everything this service puts on the clipboard. Converted text is offered with
    /// delayed rendering, so the window receives WM_RENDERFORMAT when an application actually pastes it; only after that
    /// is the user's previous clipboard content put back.
    /// Create it on the UI thread: that thread's message loop serves the window. Never block the UI thread on this
    /// service's tasks, because clipboard calls on other threads wait for the window to answer.
    /// </summary>
    public sealed partial class ClipboardService : IClipboardService, IDisposable
    {
        private const uint CF_DIB = 8;
        private const uint CF_UNICODETEXT = 13;
        private const uint CF_HDROP = 15;
        private const uint CF_LOCALE = 16;
        private const uint CF_DIBV5 = 17;
        private const uint GMEM_MOVEABLE = 0x0002;

        // Roughly one second to wait for another application to release the clipboard
        private const int OpenAttempts = 20;
        private const int OpenRetryDelayMs = 50;

        // Larger clipboard contents are not preserved: copying them on every hotkey press would be slow
        private const long MaxSnapshotBytes = 32 * 1024 * 1024;

        private static readonly uint HtmlFormat = RegisterClipboardFormat("HTML Format");
        private static readonly uint RtfFormat = RegisterClipboardFormat("Rich Text Format");
        private static readonly uint PreferredDropEffectFormat = RegisterClipboardFormat("Preferred DropEffect");

        // Documented markers that keep content out of Win+V history, cloud clipboard and clipboard managers
        private static readonly uint[] PrivacyFormats =
        {
            RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
            RegisterClipboardFormat("CanIncludeInClipboardHistory"),
            RegisterClipboardFormat("CanUploadToCloudClipboard"),
        };

        private readonly OwnerWindow _window;
        private readonly object _pendingLock = new();
        private string? _pendingText;     // promised with delayed rendering and not rendered yet
        private long _lastReadTimestamp;  // Stopwatch timestamp of the last render of our text

        public ClipboardService()
        {
            _window = new OwnerWindow(this);
        }

        public uint GetSequenceNumber() => GetClipboardSequenceNumber();

        public Task<string?> TryGetTextAsync() => Task.Run(() =>
        {
            if (!TryOpenClipboard()) return null;
            try
            {
                byte[]? data = ReadFormat(CF_UNICODETEXT, long.MaxValue, out _);
                if (data == null) return null; // the clipboard holds something else, e.g. an image or files

                string text = Encoding.Unicode.GetString(data);
                int terminator = text.IndexOf('\0');
                return terminator >= 0 ? text.Substring(0, terminator) : text;
            }
            finally
            {
                CloseClipboard();
            }
        });

        public Task<ClipboardSnapshot?> TryCaptureAsync() => Task.Run(() =>
        {
            if (!TryOpenClipboard()) return null;
            try
            {
                var items = new List<ClipboardSnapshot.Item>();
                long budget = MaxSnapshotBytes;
                foreach (uint format in RestorableFormats())
                {
                    byte[]? data = ReadFormat(format, budget, out bool tooLarge);
                    if (tooLarge)
                    {
                        AppLog.Info("The clipboard holds too much data to preserve during conversion");
                        return null;
                    }
                    if (data == null) continue;

                    budget -= data.Length;
                    items.Add(new ClipboardSnapshot.Item(format, data));
                }
                return new ClipboardSnapshot(items);
            }
            finally
            {
                CloseClipboard();
            }
        });

        public Task<bool> TrySetTextAsync(string text) => Task.Run(() =>
        {
            if (!TryOpenClipboard()) return false;
            try
            {
                // Emptying makes our window the owner; the WM_DESTROYCLIPBOARD it sends drops any older promise first
                if (!EmptyClipboard()) return LogFailure("EmptyClipboard");

                lock (_pendingLock) _pendingText = text;

                // Delayed rendering: a null handle promises the data, and WM_RENDERFORMAT tells us when it is pasted.
                // Success also returns null here, so failure is detected through the last error.
                if (SetClipboardData(CF_UNICODETEXT, IntPtr.Zero) == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
                {
                    lock (_pendingLock) _pendingText = null;
                    return LogFailure("SetClipboardData");
                }

                MarkAsPrivate();
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        });

        public async Task<bool> WaitForTextReadAsync(long sinceTimestamp, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (Volatile.Read(ref _lastReadTimestamp) < sinceTimestamp)
            {
                if (sw.Elapsed >= timeout) return false;
                await Task.Delay(20);
            }
            return true;
        }

        public Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence) => Task.Run(() =>
        {
            if (!TryOpenClipboard()) return false;
            try
            {
                // Only undo our own changes: if something else was copied in the meantime, that content wins
                bool stillOurs = GetClipboardOwner() == _window.Handle;
                if (!stillOurs && GetClipboardSequenceNumber() != expectedSequence)
                {
                    AppLog.Info("The clipboard changed during conversion, so it is not restored");
                    return false;
                }

                if (!EmptyClipboard()) return LogFailure("EmptyClipboard");

                foreach (var item in snapshot.Items)
                {
                    if (!TrySetData(item.Format, item.Data)) LogFailure($"SetClipboardData for format {item.Format}");
                }
                if (snapshot.Items.Count > 0) MarkAsPrivate();
                return true;
            }
            finally
            {
                CloseClipboard();
            }
        });

        public void Dispose()
        {
            // Destroying the owner window renders any still-promised text (WM_RENDERALLFORMATS)
            _window.DestroyHandle();
        }

        private static IEnumerable<uint> RestorableFormats()
        {
            // CF_TEXT, CF_OEMTEXT and CF_BITMAP are synthesized by Windows from these, so they are not stored
            yield return CF_UNICODETEXT;
            yield return CF_LOCALE;
            yield return HtmlFormat;
            yield return RtfFormat;
            // One bitmap format is enough: Windows converts between them, and CF_DIBV5 keeps transparency
            yield return IsClipboardFormatAvailable(CF_DIBV5) ? CF_DIBV5 : CF_DIB;
            yield return CF_HDROP;
            yield return PreferredDropEffectFormat;
        }

        // Called on the UI thread by the owner window
        private void RenderPendingText(bool shutdown)
        {
            string? text;
            lock (_pendingLock)
            {
                text = _pendingText;
                _pendingText = null;
            }
            if (text == null) return;

            // WM_RENDERFORMAT arrives while the requesting application holds the clipboard open. WM_RENDERALLFORMATS
            // (we are being destroyed) requires opening it ourselves and re-checking that we still own it.
            if (shutdown && (!OpenClipboard(_window.Handle) || GetClipboardOwner() != _window.Handle))
                return;

            try
            {
                if (TrySetData(CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + '\0')) && !shutdown)
                    Volatile.Write(ref _lastReadTimestamp, Stopwatch.GetTimestamp());
            }
            finally
            {
                if (shutdown) CloseClipboard();
            }
        }

        private void ForgetPendingText()
        {
            lock (_pendingLock) _pendingText = null;
        }

        private bool TryOpenClipboard()
        {
            // The clipboard may be briefly held by another application (Telegram, browsers, clipboard managers)
            for (int i = 0; i < OpenAttempts; i++)
            {
                if (OpenClipboard(_window.Handle)) return true;
                Thread.Sleep(OpenRetryDelayMs);
            }
            return LogFailure("OpenClipboard");
        }

        private static void MarkAsPrivate()
        {
            foreach (uint format in PrivacyFormats)
            {
                if (format != 0) TrySetData(format, new byte[4]); // DWORD 0
            }
        }

        // Reads one format as raw bytes; null if absent or larger than maxBytes (tooLarge tells which)
        private static byte[]? ReadFormat(uint format, long maxBytes, out bool tooLarge)
        {
            tooLarge = false;
            if (format == 0) return null;

            IntPtr hGlobal = GetClipboardData(format);
            if (hGlobal == IntPtr.Zero) return null;

            long size = (long)(ulong)GlobalSize(hGlobal);
            if (size > maxBytes || size > int.MaxValue)
            {
                tooLarge = true;
                return null;
            }

            IntPtr pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero) return null;
            try
            {
                var data = new byte[size];
                Marshal.Copy(pGlobal, data, 0, (int)size);
                return data;
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }
        }

        // Copies data into a movable global block and hands it to the clipboard, which owns it on success
        private static bool TrySetData(uint format, byte[] data)
        {
            if (data.Length == 0) return false;

            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
            if (hGlobal == IntPtr.Zero) return false;

            IntPtr pGlobal = GlobalLock(hGlobal);
            if (pGlobal == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }
            Marshal.Copy(data, 0, pGlobal, data.Length);
            GlobalUnlock(hGlobal);

            if (SetClipboardData(format, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }
            return true;
        }

        private static bool LogFailure(string operation)
        {
            AppLog.Error($"Clipboard: {operation} failed (Win32 error {Marshal.GetLastPInvokeError()})");
            return false;
        }

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

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetClipboardOwner();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsClipboardFormatAvailable(uint format);

        [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial uint RegisterClipboardFormat(string lpszFormat);

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

        /// <summary>Message-only window that owns our clipboard content and renders the promised text.</summary>
        private sealed class OwnerWindow : NativeWindow
        {
            private const int WM_RENDERFORMAT = 0x0305;
            private const int WM_RENDERALLFORMATS = 0x0306;
            private const int WM_DESTROYCLIPBOARD = 0x0307;
            private static readonly IntPtr HWND_MESSAGE = new(-3);

            private readonly ClipboardService _service;

            public OwnerWindow(ClipboardService service)
            {
                _service = service;
                CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_RENDERFORMAT:
                        if ((uint)m.WParam == CF_UNICODETEXT) _service.RenderPendingText(shutdown: false);
                        return;
                    case WM_RENDERALLFORMATS:
                        _service.RenderPendingText(shutdown: true);
                        return;
                    case WM_DESTROYCLIPBOARD:
                        _service.ForgetPendingText();
                        return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
