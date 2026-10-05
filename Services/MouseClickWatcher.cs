using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MovaCore.Services
{
    /// <summary>
    /// Reports mouse button presses anywhere through raw input, on a message-only window created on the UI thread.
    /// Unlike a low-level mouse hook, raw input cannot slow the mouse down, and it is registered only while asked for.
    /// <see cref="Watch"/> may be called from any thread; <see cref="Clicked"/> is raised on the UI thread.
    /// </summary>
    public sealed partial class MouseClickWatcher : NativeWindow, IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const int WM_APP_WATCH = 0x8000 + 1; // WM_APP + 1: start (wParam 1) or stop (0) watching
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEMOUSE = 0;
        private const uint RIDEV_REMOVE = 0x00000001;
        private const uint RIDEV_INPUTSINK = 0x00000100; // also while another application is in the foreground
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01, HID_USAGE_GENERIC_MOUSE = 0x02;

        // RI_MOUSE_LEFT/RIGHT/MIDDLE/BUTTON_4/BUTTON_5_DOWN
        private const ushort AnyButtonDown = 0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100;

        private static readonly IntPtr HWND_MESSAGE = new(-3);

        private bool _registered; // UI thread only

        public MouseClickWatcher()
        {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        public event EventHandler? Clicked;

        /// <summary>Starts or stops watching; the change happens on the UI thread, in the order of the calls.</summary>
        public void Watch(bool watch)
        {
            if (Handle != IntPtr.Zero) PostMessage(Handle, WM_APP_WATCH, watch ? 1 : 0, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_APP_WATCH:
                    Register(m.WParam != IntPtr.Zero);
                    return;
                case WM_INPUT:
                    if (_registered && IsButtonDown(m.LParam)) Clicked?.Invoke(this, EventArgs.Empty);
                    break; // DefWindowProc cleans up after WM_INPUT
            }
            base.WndProc(ref m);
        }

        private unsafe void Register(bool watch)
        {
            if (watch == _registered) return;
            var device = new RAWINPUTDEVICE
            {
                UsagePage = HID_USAGE_PAGE_GENERIC,
                Usage = HID_USAGE_GENERIC_MOUSE,
                Flags = watch ? RIDEV_INPUTSINK : RIDEV_REMOVE,
                Target = watch ? Handle : IntPtr.Zero, // must be null to remove
            };
            if (RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
                _registered = watch;
            else
                AppLog.Error($"Could not {(watch ? "start" : "stop")} watching mouse clicks (error {Marshal.GetLastPInvokeError()})");
        }

        private static unsafe bool IsButtonDown(IntPtr rawInput)
        {
            // RAWINPUTHEADER, then RAWMOUSE: usFlags, padding, then usButtonFlags at offset 4
            const int bufferSize = 64;
            byte* buffer = stackalloc byte[bufferSize];
            uint size = bufferSize;
            uint headerSize = (uint)sizeof(RAWINPUTHEADER);
            if (GetRawInputData(rawInput, RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue) return false;
            if (((RAWINPUTHEADER*)buffer)->Type != RIM_TYPEMOUSE || size < headerSize + 8) return false;
            ushort buttonFlags = *(ushort*)(buffer + headerSize + 4);
            return (buttonFlags & AnyButtonDown) != 0;
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            Register(false);
            DestroyHandle();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);

        [LibraryImport("user32.dll")]
        private static unsafe partial uint GetRawInputData(IntPtr rawInput, uint command, byte* data, ref uint size, uint headerSize);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessage(IntPtr hWnd, int msg, nint wParam, IntPtr lParam);
    }
}
