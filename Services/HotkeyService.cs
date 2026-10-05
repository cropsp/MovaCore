using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;
using SharpHook;
using SharpHook.Data;

namespace MovaCore.Services
{
    public partial class HotkeyService : IHotkeyService
    {
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
        private const int KeyDelayMs = 25;

        // Modifiers that may be physically held when we simulate a shortcut, with their Win32 virtual-key codes.
        // Ctrl is handled separately: our shortcuts press it first.
        private static readonly (KeyCode Key, int VirtualKey)[] HeldModifiers =
        {
            (KeyCode.VcLeftAlt, 0xA4), (KeyCode.VcRightAlt, 0xA5),
            (KeyCode.VcLeftShift, 0xA0), (KeyCode.VcRightShift, 0xA1),
            (KeyCode.VcLeftMeta, 0x5B), (KeyCode.VcRightMeta, 0x5C),
        };
        private const int VK_RCONTROL = 0xA3;

        // An unassigned virtual key (as used by AutoHotkey): tapping it while Alt or Win is down makes Windows treat
        // their release as part of a shortcut, so the app does not open its menu bar and Windows does not open Start
        private const byte MenuMaskVirtualKey = 0xE8;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private readonly IGlobalHook _hook;
        private readonly EventSimulator _simulator;
        private readonly object _captureLock = new();
        private TaskCompletionSource<Hotkey?>? _capture; // a pending CaptureHotkeyAsync
        private KeyCode _capturedKey = KeyCode.VcUndefined; // its release is suppressed too (hook thread only)
        private readonly HotkeyStateTracker _tracker = new(); // hook thread only, except Reset before the hook runs
        private Task? _runTask; // Start and Stop are called on the UI thread only

        // Set on the UI thread, read on the hook thread: replaced as a whole, never mutated
        private volatile StrongBox<Hotkey> _trigger = new(new Hotkey(KeyCode.VcF10, HotkeyModifiers.None));
        private volatile HashSet<string> _excludedProcesses = new();
        private volatile CopyPasteKeys _copyPasteKeys = CopyPasteKeys.CtrlCV;

        public event EventHandler? HotkeyTriggered;
        public event EventHandler<Exception>? HookFailed;

        public HotkeyService()
        {
            // Keyboard only: a mouse hook would route every mouse move through this process for nothing.
            // RunAsync uses a background thread, so the hook never keeps the process alive on exit.
            _hook = new SimpleGlobalHook(GlobalHookType.Keyboard, runAsyncOnBackgroundThread: true);
            _simulator = new EventSimulator();

            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
        }

        public CopyPasteKeys CopyPasteKeys
        {
            get => _copyPasteKeys;
            set => _copyPasteKeys = value;
        }

        private bool IsRunning => _runTask is { IsCompleted: false };

        public void SetTrigger(Hotkey trigger)
        {
            _trigger = new StrongBox<Hotkey>(trigger);
        }

        public void SetExcludedProcesses(IEnumerable<string> processNames)
        {
            _excludedProcesses = ProcessNames.NormalizeAll(processNames);
        }

        public void Start()
        {
            if (IsRunning) return;

            // A key held while the hook was stopped (Pause) would otherwise swallow the next press of that key
            _tracker.Reset();

            // The task completes when the hook stops, and faults if it cannot start (e.g. uiohook.dll is missing)
            _runTask = _hook.RunAsync();
            _runTask.ContinueWith(
                t =>
                {
                    Exception error = t.Exception!.GetBaseException();
                    AppLog.Error("Keyboard hook failed", error);
                    HookFailed?.Invoke(this, error);
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Stop()
        {
            Task? runTask = _runTask;
            if (runTask == null || runTask.IsCompleted) return;

            // The hook thread sets IsRunning only once it has started, and clears it only after it has stopped,
            // so wait for both: a hook that is still starting cannot be stopped, and one still stopping cannot be
            // started again. Unlike Dispose, Stop keeps the hook reusable.
            SpinWait.SpinUntil(() => _hook.IsRunning || runTask.IsCompleted, StopTimeout);
            if (_hook.IsRunning) _hook.Stop();

            try
            {
                if (!runTask.Wait(StopTimeout)) AppLog.Error("The keyboard hook did not stop in time");
            }
            catch (AggregateException)
            {
                // The failure was already logged and reported by the continuation in Start
            }
        }

        public async Task<Hotkey?> CaptureHotkeyAsync(CancellationToken cancellationToken)
        {
            var capture = new TaskCompletionSource<Hotkey?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_captureLock)
            {
                _capture?.TrySetResult(null);
                _capture = capture;
            }

            // Recording works while MovaCore is paused too: run the hook just for the recording
            bool wasRunning = IsRunning;
            if (!wasRunning) Start();
            try
            {
                using (cancellationToken.Register(() => EndCapture(capture, null)))
                {
                    return await capture.Task;
                }
            }
            finally
            {
                if (!wasRunning) Stop();
            }
        }

        private void EndCapture(TaskCompletionSource<Hotkey?> capture, Hotkey? result)
        {
            lock (_captureLock)
            {
                if (_capture == capture) _capture = null;
            }
            capture.TrySetResult(result);
        }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            KeyCode key = e.Data.KeyCode;

            TaskCompletionSource<Hotkey?>? capture = Volatile.Read(ref _capture);
            if (capture != null)
            {
                // Modifiers pass through until the main key arrives
                if (HotkeyMatching.IsModifierKey(key)) return;

                e.SuppressEvent = true;
                _capturedKey = key;
                EndCapture(capture, key == KeyCode.VcEscape ? null : new Hotkey(key, HotkeyMatching.FromMask(e.RawEvent.Mask)));
                return;
            }

            Hotkey trigger = _trigger.Value;
            HotkeyAction action = _tracker.OnKeyPressed(
                key, e.RawEvent.Mask, e.IsEventSimulated, trigger, null, IsForegroundProcessExcluded);
            if (action == HotkeyAction.PassThrough) return;

            e.SuppressEvent = true;

            // The app saw Alt/Win go down but will not see the suppressed key: mask the release of Alt/Win
            if (action == HotkeyAction.TriggerPressed &&
                (trigger.Modifiers & (HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0)
            {
                keybd_event(MenuMaskVirtualKey, 0, 0, 0);
                keybd_event(MenuMaskVirtualKey, 0, KEYEVENTF_KEYUP, 0);
            }
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            KeyCode key = e.Data.KeyCode;

            if (key == _capturedKey)
            {
                e.SuppressEvent = true;
                _capturedKey = KeyCode.VcUndefined;
                return;
            }

            // Only a release whose press was swallowed is ours; otherwise the application gets it
            HotkeyAction action = _tracker.OnKeyReleased(key);
            if (action == HotkeyAction.PassThrough) return;

            e.SuppressEvent = true;
            if (action == HotkeyAction.TriggerReleased) HotkeyTriggered?.Invoke(this, EventArgs.Empty);
        }

        private bool IsForegroundProcessExcluded()
        {
            HashSet<string> excluded = _excludedProcesses;
            if (excluded.Count == 0) return false;

            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
                if (processId == 0) return false;

                using var process = Process.GetProcessById((int)processId);
                return excluded.Contains(ProcessNames.Normalize(process.ProcessName));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false; // the process has just exited
            }
        }

        public void SimulateCopy()
        {
            if (_copyPasteKeys == CopyPasteKeys.CtrlInsertShiftInsert)
                SendShortcut(KeyCode.VcInsert, 1, KeyCode.VcLeftControl);
            else
                SendShortcut(KeyCode.VcC, 1, KeyCode.VcLeftControl);
        }

        public void SimulatePaste()
        {
            if (_copyPasteKeys == CopyPasteKeys.CtrlInsertShiftInsert)
                SendShortcut(KeyCode.VcInsert, 1, KeyCode.VcLeftShift);
            else
                SendShortcut(KeyCode.VcV, 1, KeyCode.VcLeftControl);
        }

        public void SimulateSelectLeft(int caretSteps)
        {
            if (caretSteps > 0) SendShortcut(KeyCode.VcLeft, caretSteps, KeyCode.VcLeftShift);
        }

        public void SimulateSelectWordLeft()
        {
            SendShortcut(KeyCode.VcLeft, 1, KeyCode.VcLeftControl, KeyCode.VcLeftShift);
        }

        // Presses `modifiers`, taps `key` `repeat` times and releases the modifiers, regardless of what the user holds
        private void SendShortcut(KeyCode key, int repeat, params KeyCode[] modifiers)
        {
            // Ctrl goes down first: it masks the release of a held Alt or Win below (no menu bar, no Start menu)
            _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            ReleaseHeldModifiers();

            if (Array.IndexOf(modifiers, KeyCode.VcLeftControl) < 0)
            {
                _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
                if (GetAsyncKeyState(VK_RCONTROL) < 0) _simulator.SimulateKeyRelease(KeyCode.VcRightControl);
            }
            foreach (KeyCode modifier in modifiers)
            {
                if (modifier != KeyCode.VcLeftControl) _simulator.SimulateKeyPress(modifier);
            }
            Thread.Sleep(KeyDelayMs);

            for (int i = 0; i < repeat; i++)
            {
                _simulator.SimulateKeyPress(key);
                _simulator.SimulateKeyRelease(key);
            }
            Thread.Sleep(KeyDelayMs);

            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                _simulator.SimulateKeyRelease(modifiers[i]);
            }
        }

        private void ReleaseHeldModifiers()
        {
            // A held Alt, Shift or Win would turn our shortcut into a different one. Release only keys that are
            // actually down: a key-up for a key that is not pressed is a stray event for the focused application.
            bool released = false;
            foreach (var (key, virtualKey) in HeldModifiers)
            {
                if (GetAsyncKeyState(virtualKey) < 0)
                {
                    _simulator.SimulateKeyRelease(key);
                    released = true;
                }
            }

            if (released) Thread.Sleep(KeyDelayMs);
        }

        public void Dispose()
        {
            _hook.Dispose();
        }

        [LibraryImport("user32.dll")]
        private static partial short GetAsyncKeyState(int vKey);

        [LibraryImport("user32.dll")]
        private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
