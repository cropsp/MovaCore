using System;
using SharpHook;
using SharpHook.Native;

namespace LayoutConverter.App.Services
{
    public interface IHotkeyService : IDisposable
    {
        void Start();
        void Stop();
        event EventHandler HotkeyTriggered;
        void SimulateCopy();
        void SimulatePaste();
    }

    public class HotkeyService : IHotkeyService
    {
        private readonly IGlobalHook _hook;
        private readonly EventSimulator _simulator;
        private bool _isAltPressed;

        public event EventHandler HotkeyTriggered;

        public HotkeyService()
        {
            _hook = new SimpleGlobalHook();
            _simulator = new EventSimulator();

            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
        }

        public void Start()
        {
            System.Threading.Tasks.Task.Run(() => _hook.Run());
        }

        public void Stop()
        {
            _hook.Dispose();
        }

        private void OnKeyPressed(object sender, KeyboardHookEventArgs e)
        {
            // Відслідковуємо затиснення Alt
            if (e.Data.KeyCode == KeyCode.VcLeftAlt || e.Data.KeyCode == KeyCode.VcRightAlt)
            {
                _isAltPressed = true;
            }

            // Хоткей: Alt + Q 
            if (_isAltPressed && e.Data.KeyCode == KeyCode.VcQ)
            {
                HotkeyTriggered?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnKeyReleased(object sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcLeftAlt || e.Data.KeyCode == KeyCode.VcRightAlt)
            {
                _isAltPressed = false;
            }
        }

        public void SimulateCopy()
        {
            _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            _simulator.SimulateKeyPress(KeyCode.VcC);
            _simulator.SimulateKeyRelease(KeyCode.VcC);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
        }

        public void SimulatePaste()
        {
            _simulator.SimulateKeyPress(KeyCode.VcLeftControl);
            _simulator.SimulateKeyPress(KeyCode.VcV);
            _simulator.SimulateKeyRelease(KeyCode.VcV);
            _simulator.SimulateKeyRelease(KeyCode.VcLeftControl);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}