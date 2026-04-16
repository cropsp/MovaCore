using System;
using System.Threading;
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
        private bool _isF10Down = false; // State tracking for auto-repeat protection

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
            // Хоткей: F10
            if (e.Data.KeyCode == KeyCode.VcF10)
            {
                // Придушуємо подію завжди, щоб вона не потрапляла в систему (не відкривала меню)
                e.SuppressEvent = true;
                _isF10Down = true;
            }
        }

        private void OnKeyReleased(object sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcF10)
            {
                // Придушуємо і відпускання теж
                e.SuppressEvent = true;

                if (_isF10Down)
                {
                    _isF10Down = false;
                    // Тригеримо роботу ТІЛЬКИ коли клавішу відпущено
                    HotkeyTriggered?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void ReleaseModifiers()
        {
            // Примусово відпускаємо модифікатори
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