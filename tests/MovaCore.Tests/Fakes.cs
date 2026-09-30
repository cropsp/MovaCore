using System;
using System.Threading.Tasks;
using LayoutConverter.App.Services;
using SharpHook.Data;

namespace MovaCore.Tests
{
    public class FakeClipboard : IClipboardService
    {
        public uint Sequence { get; set; }
        public string? Text { get; set; }
        public bool FailSet { get; set; }

        public int GetCalls { get; set; }
        public int SetCalls { get; set; }

        public uint GetSequenceNumber() => Sequence;

        public Task<string?> TryGetTextAsync()
        {
            GetCalls++;
            return Task.FromResult(Text);
        }

        public Task<bool> TrySetTextAsync(string text)
        {
            SetCalls++;
            if (FailSet) return Task.FromResult(false);

            Text = text;
            Sequence++;
            return Task.FromResult(true);
        }

        /// <summary>What the foreground application does when it handles Ctrl+C.</summary>
        public void SimulateAppCopy(string? text)
        {
            Text = text;
            Sequence++;
        }
    }

    public class FakeHotkeyService : IHotkeyService
    {
        public Action? OnCopy { get; set; }

        public int CopyCalls { get; set; }
        public int PasteCalls { get; set; }

        // Never raised by the fake, so the accessors are intentionally empty.
        public event EventHandler? HotkeyTriggered
        {
            add { }
            remove { }
        }

        public void Start() { }
        public void Stop() { }
        public void SetTriggerKey(KeyCode key) { }

        public void SimulateCopy()
        {
            CopyCalls++;
            OnCopy?.Invoke();
        }

        public void SimulatePaste()
        {
            PasteCalls++;
        }

        public void Dispose() { }
    }
}
