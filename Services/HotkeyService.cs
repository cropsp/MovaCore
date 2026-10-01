using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Data;

namespace MovaCore.Services
{
    public partial class HotkeyService : IHotkeyService
    {
        // Modifiers that may be physically held when the trigger fires, with their Win32 virtual-key codes
        private static readonly (KeyCode Key, int VirtualKey)[] Modifiers =
        {
            (KeyCode.VcLeftAlt, 0xA4), (KeyCode.VcRightAlt, 0xA5),
            (KeyCode.VcLeftShift, 0xA0), (KeyCode.VcRightShift, 0xA1),
            (KeyCode.VcLeftMeta, 0x5B), (KeyCode.VcRightMeta, 0x5C),
        };

        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

        private readonly IGlobalHook _hook;
        private readonly EventSimulator _simulator;
        private bool _isTriggerKeyDown; // only touched on the hook thread
        private volatile KeyCode _triggerKey = KeyCode.VcF10; // set on the UI thread, read on the hook thread
        private Task? _runTask; // Start and Stop are called on the UI thread only

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

        public void SetTriggerKey(KeyCode key)
        {
            _triggerKey = key;
        }

        public void Start()
        {
            if (_runTask is { IsCompleted: false }) return; // already running

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

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == _triggerKey)
            {
                e.SuppressEvent = true;
                _isTriggerKeyDown = true;
            }
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == _triggerKey)
            {
                e.SuppressEvent = true;

                if (_isTriggerKeyDown)
                {
                    _isTriggerKeyDown = false;
                    HotkeyTriggered?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void ReleaseModifiers()
        {
            // A held Alt, Shift or Win would turn Ctrl+C/Ctrl+V into a different shortcut. Release only keys that are
            // actually down: a key-up for a key that is not pressed is a stray event for the focused application.
            bool released = false;
            foreach (var (key, virtualKey) in Modifiers)
            {
                if (GetAsyncKeyState(virtualKey) < 0)
                {
                    _simulator.SimulateKeyRelease(key);
                    released = true;
                }
            }

            if (released) Thread.Sleep(20);
        }

        public void SimulateCopy()
        {
            ReleaseModifiers();

            _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            Thread.Sleep(25);
            _simulator.SimulateKeyPress(KeyCode.VcC);
            Thread.Sleep(25);
            _simulator.SimulateKeyRelease(KeyCode.VcC);
            Thread.Sleep(25);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
        }

        public void SimulatePaste()
        {
            ReleaseModifiers();

            _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            Thread.Sleep(25);
            _simulator.SimulateKeyPress(KeyCode.VcV);
            Thread.Sleep(25);
            _simulator.SimulateKeyRelease(KeyCode.VcV);
            Thread.Sleep(25);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
        }

        public void Dispose()
        {
            _hook.Dispose();
        }

        [LibraryImport("user32.dll")]
        private static partial short GetAsyncKeyState(int vKey);
    }
}
