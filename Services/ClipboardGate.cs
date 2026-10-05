using System;
using System.Threading;
using System.Threading.Tasks;

namespace MovaCore.Services
{
    /// <summary>
    /// Lets one operation at a time take over the clipboard and simulate keys: a conversion and a dictation paste
    /// must never interleave. A conversion gives up if the gate is taken; a dictation paste waits for it.
    /// </summary>
    public sealed class ClipboardGate
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public bool TryEnter() => _semaphore.Wait(0);

        public Task<bool> WaitAsync(TimeSpan timeout) => _semaphore.WaitAsync(timeout);

        public void Exit() => _semaphore.Release();
    }
}
