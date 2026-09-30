using System;
using System.Threading;
using SharpHook;
using SharpHook.Data;

namespace LayoutConverter.App.Services
{
    public interface IHotkeyService : IDisposable
    {
        void Start();
        void Stop();
        void SetTriggerKey(KeyCode key);
        event EventHandler? HotkeyTriggered;
        void SimulateCopy();
        void SimulatePaste();
    }

    public class HotkeyService : IHotkeyService
    {
        private readonly IGlobalHook _hook;
        private readonly EventSimulator _simulator;
        private bool _isTriggerKeyDown = false;
        private KeyCode _triggerKey = KeyCode.VcF10; // Default

        public event EventHandler? HotkeyTriggered;

        public HotkeyService()
        {
            _hook = new SimpleGlobalHook();
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
            // Run() blocks until the hook is disposed; if it cannot start (e.g. uiohook.dll is missing), log why
            System.Threading.Tasks.Task.Run(() => _hook.Run())
                .ContinueWith(
                    t => AppLog.Error("Keyboard hook failed", t.Exception?.GetBaseException()),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Stop()
        {
            _hook.Dispose();
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
            _simulator.SimulateKeyRelease(KeyCode.VcLeftAlt);
            _simulator.SimulateKeyRelease(KeyCode.VcRightAlt);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftShift);
            _simulator.SimulateKeyRelease(KeyCode.VcRightShift);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftMeta);
            _simulator.SimulateKeyRelease(KeyCode.VcRightMeta);

            Thread.Sleep(20);
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
            Stop();
        }
    }
}
