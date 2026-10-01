using System;
using SharpHook.Data;

namespace MovaCore.Services
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
}
