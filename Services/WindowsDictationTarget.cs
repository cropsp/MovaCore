using System;
using System.Runtime.InteropServices;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// The focused control of the foreground window (GetGUIThreadInfo), with key presses from the hook and mouse clicks
    /// from raw input as the moments the user may have moved the caret. Create it on the UI thread.
    /// </summary>
    public sealed partial class WindowsDictationTarget : IDictationTarget, IDisposable
    {
        private readonly IHotkeyService _hotkeys;
        private readonly MouseClickWatcher _clicks = new();

        public WindowsDictationTarget(IHotkeyService hotkeys)
        {
            _hotkeys = hotkeys;
            _hotkeys.UserKeyPressed += OnInterrupted;
            _clicks.Clicked += OnInterrupted;
        }

        public event EventHandler? Interrupted;

        public DictationFocus? GetFocus()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return null;

            var info = new GUITHREADINFO { Size = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            uint thread = GetWindowThreadProcessId(foreground, out _);
            if (thread == 0 || !GetGUIThreadInfo(thread, ref info)) return null;
            return new DictationFocus(foreground, info.Focus);
        }

        public void WatchClicks(bool watch) => _clicks.Watch(watch);

        private void OnInterrupted(object? sender, EventArgs e) => Interrupted?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            _hotkeys.UserKeyPressed -= OnInterrupted;
            _clicks.Clicked -= OnInterrupted;
            _clicks.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public uint Size;
            public uint Flags;
            public IntPtr Active;
            public IntPtr Focus;
            public IntPtr Capture;
            public IntPtr MenuOwner;
            public IntPtr MoveSize;
            public IntPtr Caret;
            public int CaretLeft, CaretTop, CaretRight, CaretBottom;
        }

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    }
}
