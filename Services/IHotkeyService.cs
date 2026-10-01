using System;
using SharpHook.Data;

namespace MovaCore.Services
{
    public interface IHotkeyService : IDisposable
    {
        /// <summary>Starts the global keyboard hook. Can be called again after <see cref="Stop"/>.</summary>
        void Start();

        /// <summary>Stops the hook without disposing it.</summary>
        void Stop();

        void SetTriggerKey(KeyCode key);

        /// <summary>Raised on the hook thread when the trigger key is released.</summary>
        event EventHandler? HotkeyTriggered;

        /// <summary>Raised on a worker thread when the hook cannot start or stops with an error.</summary>
        event EventHandler<Exception>? HookFailed;

        void SimulateCopy();
        void SimulatePaste();
    }
}
