using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;
using MovaCore.Services;

namespace MovaCore.Tests
{
    public class FakeClipboard : IClipboardService
    {
        private const uint UnicodeTextFormat = 13;

        public uint Sequence { get; set; }
        public string? Text { get; set; }
        public bool FailSet { get; set; }

        /// <summary>Whether the app is seen reading the converted text after Ctrl+V (WM_RENDERFORMAT in reality).</summary>
        public bool PasteObserved { get; set; } = true;

        /// <summary>True while the clipboard holds content written by us, as GetClipboardOwner would report.</summary>
        public bool OwnedByUs { get; private set; }

        public string? LastSetText { get; private set; }
        public uint? RestoredExpectedSequence { get; private set; }

        public int GetCalls { get; set; }
        public int SetCalls { get; set; }
        public int CaptureCalls { get; private set; }
        public int RestoreCalls { get; private set; }

        public uint GetSequenceNumber() => Sequence;

        public Task<string?> TryGetTextAsync()
        {
            GetCalls++;
            return Task.FromResult(Text);
        }

        public Task<ClipboardSnapshot?> TryCaptureAsync()
        {
            CaptureCalls++;
            var items = Text == null
                ? Array.Empty<ClipboardSnapshot.Item>()
                : new[] { new ClipboardSnapshot.Item(UnicodeTextFormat, Encoding.Unicode.GetBytes(Text)) };
            return Task.FromResult<ClipboardSnapshot?>(new ClipboardSnapshot(items));
        }

        public Task<bool> TrySetTextAsync(string text)
        {
            SetCalls++;
            if (FailSet) return Task.FromResult(false);

            Text = text;
            LastSetText = text;
            OwnedByUs = true;
            Sequence++;
            return Task.FromResult(true);
        }

        public Task<bool> WaitForTextReadAsync(long sinceTimestamp, TimeSpan timeout) => Task.FromResult(PasteObserved);

        public Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence)
        {
            RestoreCalls++;
            RestoredExpectedSequence = expectedSequence;
            if (!OwnedByUs && Sequence != expectedSequence) return Task.FromResult(false);

            Text = snapshot.Items.Count == 0 ? null : Encoding.Unicode.GetString(snapshot.Items[0].Data);
            OwnedByUs = true;
            Sequence++;
            return Task.FromResult(true);
        }

        /// <summary>What the foreground application does when it handles Ctrl+C.</summary>
        public void SimulateAppCopy(string? text)
        {
            Text = text;
            OwnedByUs = false;
            Sequence++;
        }
    }

    public class FakeHotkeyService : IHotkeyService
    {
        public Action? OnCopy { get; set; }
        public Action? OnSelectWordLeft { get; set; }

        public int CopyCalls { get; set; }
        public int PasteCalls { get; set; }
        public int SelectWordLeftCalls { get; private set; }
        public List<int> SelectLeftCalls { get; } = new();

        public Hotkey? Trigger { get; private set; }
        public Hotkey? SpeechHotkey { get; private set; }
        public List<string> ExcludedProcesses { get; } = new();
        public CopyPasteKeys CopyPasteKeys { get; set; }

        // Never raised by the fake, so the accessors are intentionally empty.
        public event EventHandler? HotkeyTriggered
        {
            add { }
            remove { }
        }

        public event EventHandler<Exception>? HookFailed
        {
            add { }
            remove { }
        }

        public event EventHandler? SpeechHotkeyPressed
        {
            add { }
            remove { }
        }

        public event EventHandler? SpeechHotkeyReleased
        {
            add { }
            remove { }
        }

        public void Start() { }
        public void Stop() { }
        public void SetTrigger(Hotkey trigger) => Trigger = trigger;
        public void SetSpeechHotkey(Hotkey? hotkey) => SpeechHotkey = hotkey;

        public void SetExcludedProcesses(IEnumerable<string> processNames)
        {
            ExcludedProcesses.Clear();
            ExcludedProcesses.AddRange(processNames);
        }

        public Task<Hotkey?> CaptureHotkeyAsync(CancellationToken cancellationToken) => Task.FromResult<Hotkey?>(null);

        public void SimulateCopy()
        {
            CopyCalls++;
            OnCopy?.Invoke();
        }

        public void SimulatePaste()
        {
            PasteCalls++;
        }

        public void SimulateSelectLeft(int caretSteps) => SelectLeftCalls.Add(caretSteps);

        public void SimulateSelectWordLeft()
        {
            SelectWordLeftCalls++;
            OnSelectWordLeft?.Invoke();
        }

        public void Dispose() { }
    }

    public class FakeLayoutSwitcher : IKeyboardLayoutSwitcher
    {
        public List<KeyboardLanguage> Switches { get; } = new();

        public bool SwitchForegroundWindowTo(KeyboardLanguage language)
        {
            Switches.Add(language);
            return true;
        }
    }
}
