# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

MovaCore is a Windows-only system-tray utility that converts selected text typed in the wrong keyboard layout
(EN ↔ UA, e.g. `ghbdsn` → `привіт`) when the user presses a global hotkey (default F10). It also does hold-to-talk
dictation: hold a second hotkey (default ScrollLock), speak, and the speech is recognized locally with Whisper and
pasted. It is written in C# on .NET 10 WinForms and shipped as a Native AOT `MovaCore.exe` plus native libraries that
Native AOT cannot embed (SharpHook's `uiohook.dll`, the Whisper runtimes in `runtimes\`), so releases are zip archives.
The root namespace is `MovaCore`.

A prioritized review of known bugs and the roadmap lives in `docs/IMPROVEMENT_PLAN.md` (in Ukrainian); check it
before changing behaviour, since many "odd" things in the code are already catalogued there.

## Commands

The app targets `net10.0-windows` with `UseWindowsForms`; the test project targets plain `net10.0` and runs anywhere.
`global.json` pins the .NET 10 SDK. `Directory.Build.props` turns warnings into errors for both projects.

```powershell
dotnet build MovaCore.csproj                           # debug build (Windows)
dotnet run --project MovaCore.csproj                   # run (appears only as a tray icon)
./publish.ps1                                          # Native AOT publish, win-x64
dotnet publish MovaCore.csproj -c Release -r win-x64   # same as publish.ps1
# output: bin/Release/net10.0-windows/win-x64/publish/ (MovaCore.exe + uiohook.dll)
MovaCore.exe --smoke-test                              # exercise AOT-sensitive paths and exit (SmokeTest.cs)
MovaCore.exe --smoke-test --smoke-test-model ggml-tiny.bin --smoke-test-audio hello.wav  # also transcribe (as CI)

dotnet test tests/MovaCore.Tests/MovaCore.Tests.csproj                                       # all tests
dotnet test tests/MovaCore.Tests/MovaCore.Tests.csproj --filter "FullyQualifiedName~HotkeyOrchestratorTests"  # one class
dotnet format MovaCore.sln --verify-no-changes         # formatting check, as in CI
```

Compile-checking the app on Linux: with Microsoft's official SDK, add `-p:EnableWindowsTargeting=true`. Distro
source-built SDKs (e.g. Ubuntu's `dotnet-sdk-10.0`) lack the WindowsDesktop SDK and fail with `MSB4019`; for those, use
the shim in `eng/LinuxCompileCheck.targets` (command in the file; for `dotnet format`, pass the same three properties
as environment variables). Either way the output is not runnable.

CI (`.github/workflows/ci.yml`, Windows runners) is the only place the real app is built, AOT-published (x64 and
arm64) and smoke-tested; its job summary lists the publish output sizes and the AOT/trim warnings (all currently from
WinForms itself). After publishing, `eng/copy-vc-runtime.ps1` copies the VC++ runtime next to the Whisper DLLs and
`eng/check-native-deps.ps1` fails if any shipped binary imports a DLL a clean PC may lack (`publish.ps1` runs both
too). The smoke test transcribes a synthesized "hello world" with the cached `ggml-tiny.bin`. Pushing a `v*` tag creates a draft release with zip archives and SHA256 sums; the tag must match
`<Version>` in `MovaCore.csproj`, and the notes come from that version's `CHANGELOG.md` section
(`docs/RELEASING.md`).

## Architecture

`Program.cs` is the composition root: a handful of long-lived objects wired by hand (no DI container). It also owns
process-wide concerns: the log (`Services/AppLog.cs`, `%LOCALAPPDATA%\MovaCore\logs`, one rotated file), handlers for
unhandled exceptions, the single-instance mutex, and an up-front check that `uiohook.dll` loads. Never log clipboard
text or keystrokes. `SmokeTest.cs` counts on `AppLog.ErrorCount`, so report failures through `AppLog.Error` rather
than swallowing them.

The conversion pipeline runs across these files:

1. **`Services/HotkeyService.cs`** runs a keyboard-only SharpHook `SimpleGlobalHook` via `RunAsync` on a background
   thread. The trigger is a `Models/Hotkey` (key + exact modifiers, matched by `HotkeyMatching`); it suppresses both
   press and release of the trigger key and raises `HotkeyTriggered` on *release*, unless the foreground process is
   in the excluded list. The press/release decisions (auto-repeat, which hotkey a release belongs to, the speech
   hotkey's `SpeechHotkeyPressed`/`SpeechHotkeyReleased`) live in the platform-neutral `HotkeyStateTracker`. Hook callbacks run synchronously on the hook thread: `e.SuppressEvent` must be set there,
   and handlers must return fast. Our own simulated events never count as the trigger. When the trigger includes Alt
   or Win, an unassigned key (VK 0xE8) is tapped so their release opens neither the app's menu nor Start; simulated
   shortcuts likewise press Ctrl *before* releasing held modifiers. `CaptureHotkeyAsync` records a new hotkey through
   the hook (used by the settings form). `Stop` keeps the hook reusable (it waits until the hook thread has really
   stopped); `HookFailed` reports a hook that cannot start. The same class simulates copy/paste (Ctrl+C/V or
   Ctrl+Insert/Shift+Insert) and selection (Shift+Left, Ctrl+Shift+Left).
2. **`TrayApplicationContext.cs`** owns the tray icon, the settings dialog and the lifetime. It forwards
   `HotkeyTriggered` to the orchestrator via `Task.Run`, so conversion never runs on the UI or hook thread, and it
   marshals worker-thread events (`ConversionFailed`, `HookFailed`) back to the UI thread before touching `NotifyIcon`.
3. **`Services/HotkeyOrchestrator.cs`** performs the clipboard round-trip: capture a snapshot of the user's clipboard →
   simulate Ctrl+C → wait (up to `CopyTimeout`) for the clipboard sequence number to change → read and convert →
   put the converted text on the clipboard → simulate Ctrl+V → wait until the target app reads it → re-select the
   pasted text (single-line, ≤ 300 text elements) → switch the window's layout to the target language → restore the
   snapshot. With `ConvertLastWord` (off by default) an empty copy is retried after Ctrl+Shift+Left. If the sequence number does not change, nothing was selected and the clipboard is left alone. The
   snapshot is restored only after the paste is observed: restoring earlier would paste the old content instead.
   A second hotkey press while a conversion runs is dropped (`ClipboardGate.TryEnter`, shared with dictation).
   `ConversionFailed` reports errors (a tray balloon) in addition to the log.
4. **`Services/ClipboardService.cs`** is raw Win32 P/Invoke (`LibraryImport`), deliberately not
   `System.Windows.Forms.Clipboard` (COM/STA issues). A message-only `NativeWindow` created on the UI thread owns
   everything we put on the clipboard. Converted text is offered with *delayed rendering*, so the window gets
   `WM_RENDERFORMAT` when an app pastes it (`WaitForTextReadAsync`). Our writes carry the
   `ExcludeClipboardContentFromMonitorProcessing` family of formats, so Win+V history and clipboard managers ignore
   them. Snapshots keep only a whitelist of formats (text, HTML, RTF, DIB, file lists) up to 32 MB. A restore happens
   only if the clipboard still holds our text or is unchanged since the copy. Clipboard calls on other threads wait
   for the UI thread to answer window messages, so never block the UI thread on this service's tasks.
5. **`Services/LayoutConverterService.cs`** holds two one-way maps (EN→UA, UA→EN) built from the paired
   `EnKeys`/`UaKeys` strings (same physical key at the same index). The direction is chosen once per string by
   counting characters that exist in only one layout; applying a single map to the whole string keeps conversion
   reversible (`Convert(Convert(s)) == s`); `TargetOf` tells which language the result is in. `KeyboardLayouts`
   (Windows-only) builds the key pairs from the installed English and Ukrainian layouts with `ToUnicodeEx`, and
   `LayoutTableBuilder` turns them into tables, filling gaps from the built-in US/"Ukrainian (Enhanced)" ones (the
   smoke test checks the built-in tables against the real layout files). The paired strings
   must stay the same length without duplicates; the constructor throws otherwise. It also switches the foreground
   window's layout (`WM_INPUTLANGCHANGEREQUEST`).

Dictation (hold-to-talk) reuses the hook, the clipboard service and the paste:

- **`Services/SpeechOrchestrator.cs`** (platform-neutral, tested) turns hotkey press/release into commands on one
  `Channel` loop, so the hook thread never blocks and their order holds: press → `IAudioRecorder.Start` and a model
  preload; release → 150 ms more recording (`TrailingAudio`), then stop, discard short (< 0.3 s) recordings and
  those without a signal (peak < −60 dBFS: `NoSignal`), keep the speech found by `ISpeechDetector` (`NoSpeech` if
  none; without a detector the energy threshold `AudioSamples.IsSilent` decides: Whisper invents text for silence),
  pad to 1.25 s, `ISpeechRecognizer.TranscribeAsync` beside the loop, `TranscriptText.Clean` (also drops
  hesitations and 3+ repeated words), then **`TextPaster`** (snapshot, set, Ctrl+V, wait for the read, restore; a
  read before the paste, e.g. by a clipboard manager, hides the paste itself, so it then restores after a pause; it
  waits for the `ClipboardGate` instead of dropping the text). `NoSpeech`/`NoSignal` are reported only after a hold
  of ≥ 1 s. It keeps the microphone open for 30 s after a dictation (`KeepMicrophoneOpen`), loads the model when
  configured and frees it when dictation is turned off or the model file is gone, enforces a 2-minute limit (a
  release during a UAC prompt is never seen) and reports `StateChanged` (state, outcome, `SpeechError`) on a worker
  thread. Before pasting, `TranscriptJoiner` fits the phrase to `DictationContext`: the last dictated text, valid
  only while the focus (`IDictationTarget`, i.e. `WindowsDictationTarget`: `GetGUIThreadInfo`) is unchanged and the
  user has neither pressed a key (`IHotkeyService.UserKeyPressed`) nor clicked (`MouseClickWatcher`, raw input,
  registered only while there is such a text); then a space goes before it, and a small letter mid-sentence.
- **`Services/WasapiAudioRecorder.cs`** (Windows-only) records through NAudio's `WasapiRecorder` in shared mode with
  AutoConvertPcm, so the audio engine delivers 16 kHz mono float; microphones are stored by endpoint ID. While open
  it keeps the last 0.5 s in a ring: a recording on an open microphone starts with 0.3 s from before the press, and
  `CopyRecent` feeds the equalizer. `Start` reopens it if the device (or the Windows default) changed or capture died.
- **`Services/WhisperSpeechRecognizer.cs`** (Windows-only) wraps Whisper.net. Whisper.net caches its native load
  result (even a failure) for the process, so the runtime is chosen and test-loaded here first and then forced
  (`RuntimeOptions.ForcedRuntimeLibrary`): Vulkan if the GPU option is on and `vulkan-1.dll` loads (with
  `VK_LOADER_LAYERS_DISABLE=~implicit~`: overlay layers crash Vulkan apps), else the CPU, and on x64 only after an
  AVX2/FMA/F16C check (ggml's CPU code dies with an illegal instruction without them). A model loaded on the GPU gets
  one warm-up run (shader compilation). It is also the `ISpeechDetector`: whisper.cpp's Silero VAD with
  `models\ggml-silero-v6.2.0.bin` next to the exe, which `eng/get-vad-model.ps1` puts into the publish output (CI and
  `publish.ps1`; a plain `dotnet build` has none, so the energy threshold is used).
- **`Services/ModelDownloader.cs`** is the only network code: it downloads a `SpeechModelCatalog` model from
  Hugging Face into `<file>.partial` (range requests resume it), follows redirects by hand to read the SHA-256 in
  `X-Linked-Etag`, checks size, hash (pinned in the catalog: whisper.cpp's SHA-1, or for q8_0 the SHA-256 recorded from Hugging Face)
  and the ggml magic (`SpeechModelFile`).
  `ModelDownloadManager` runs one download in the background; enabling dictation starts it (and startup resumes it),
  except for a model the user deleted (`Delete`, the Voice tab's button) in this session.
- `TrayApplicationContext` swaps the tray icon (red/amber dot) and drives `UI/RecordingOverlay.cs`, a click-through
  window that never takes the focus (`WS_EX_NOACTIVATE`, `ShowWithoutActivation`). It draws `UI/MouseScene.cs`: the
  logo's field mouse in grass whose tufts are the equalizer (`SpectrumAnalyzer`, Handy's algorithm), grey with the
  mouse dozing until the microphone delivers, the mouse gnawing wheat if transcribing takes over 0.3 s, a wink when
  pasted, or the mouse's head beside a short message; it fades in and out. The scene was designed as a browser mockup
  first; keep its proportions (152 x 44 logical pixels) when changing it. Errors the user can fix
  (`SpeechException`: no model, no microphone, unsupported CPU…) are logged as Info, not Error; never log what was
  said or the audio.

Settings: `Services/SettingsService.cs` stores `Models/AppSettings` as JSON in `%APPDATA%\MovaCore\settings.json`
(atomic write) and never shows UI; `Save` throws and the caller reports it. The Windows autostart entry
(`HKCU\...\Run`) lives behind `IStartupRegistration` (`StartupRegistration.cs`) and is the source of truth for
`LaunchAtStartup`; `Load` re-points it at the running exe only when the registered exe no longer exists.
`UI/SettingsForm.cs` is built in code (no designer file) from auto-sizing `TableLayoutPanel`s with
`AutoScaleMode.Dpi`, on three tabs (Layout, Voice, General); a `TabControl` does not size itself, so
`FitTabsToPages` sizes it from the largest page. Both hotkeys are recorded by `UI/HotkeyPicker` through
`IHotkeyService.CaptureHotkeyAsync`, which refuses a combination the other hotkey uses. `OnSaveClick` builds a new
`AppSettings`: a field it does not copy resets to its default. The tray offers Settings, Pause
(stops the hook; not persisted), About and Exit, and only one settings window at a time.

All user-visible text lives in `Strings.cs` (English and Ukrainian, chosen by `AppSettings.Language`, where `Auto`
follows the Windows display language via `UI/WindowsLanguage.cs`); an in-code table keeps it simple under AOT.
Add new texts to `Strings` in both languages (`StringsTests` checks that none is empty). Do not turn on
`InvariantGlobalization`: WinForms builds a `CultureInfo` for the keyboard layout whenever the user switches layouts
in one of our windows, and that throws in invariant mode.

The tray icons (normal, recording, transcribing), exe icon and settings logo are embedded resources
(`AppResources.cs`) generated from `Resources/mouse_icon.png` by `eng/generate-icons.py`; regenerate them instead of
editing the `.ico`/`.png` by hand.

One top-level type per file; interfaces live next to their implementations in `Services/`.

Tests (`tests/MovaCore.Tests`, xUnit) cannot reference the WinForms app, so the csproj compiles the platform-neutral
files in as linked sources (and `internal` members are visible to tests). `HotkeyService`, `ClipboardService`, `WasapiAudioRecorder`
and `WhisperSpeechRecognizer` are Windows-only and are not linked: tests use fakes (`tests/MovaCore.Tests/Fakes.cs`,
`SpeechFakes.cs` with a fake HTTP handler for the downloader), and the real behaviour is covered by the smoke test in
CI. Keep WinForms/registry code out of linked files, and add a link when a new testable
file appears. The app csproj excludes `tests/**` from its default globs because the app project sits at the root.

## Native AOT constraints

- Anything serialized with `System.Text.Json` must go through the source-generated `SettingsJsonContext`
  (`Models/AppSettings.cs`); add new types to its `[JsonSerializable]` list. Reflection-based serialization breaks
  under AOT.
- WinForms is not officially AOT-supported: `_SuppressWinFormsTrimError` forces the publish, and ILC warnings (all
  from WinForms today) are kept non-fatal with `IlcTreatWarningsAsErrors=false`. A new IL warning naming MovaCore,
  SharpHook, NAudio, Whisper.net or System.Net in the CI summary is a real problem. The one known exception is IL3000
  in Whisper.net's `NativeLibraryLoader` (`Assembly.Location` is empty under AOT); it then probes
  `AppDomain.BaseDirectory`, which is the exe's folder. A clean `dotnet build` proves nothing about the published exe;
  verify UI paths against the AOT-published binary.
- WinForms and Native AOT are verified only by running the published exe (`--smoke-test`, run by CI). When you touch
  UI, resources, P/Invoke or DI registration, extend `SmokeTest.cs` if the new path is not exercised.
- SharpHook `KeyCode` values are not contiguous (e.g. `VcF12 = 0x7B` but `VcF13 = 0xF000`), so never compute key
  codes arithmetically.
- Whisper.net is 1.9.2-preview1: the first version with `RuntimeOptions.ForcedRuntimeLibrary`, without which its AVX
  check (compile-time `false` under AOT on x64) rejects the CPU runtime. NAudio.Wasapi is pinned to exactly 3.1.0, its
  first release whose COM interop and structs survive AOT. The Whisper runtime packages copy every architecture's
  files; a target in `MovaCore.csproj` prunes the foreign ones after publish.
- The Whisper DLLs import the VC++ runtime, and Whisper.net loads them by full path, so Windows looks for msvcp140 etc.
  in each `runtimes\...` folder (and System32), never next to the exe: the copies go into those folders.
- SharpHook is pinned to 7.1.x. Version 8 renumbers `KeyCode` and reworks the simulation API; settings store keys by
  name since v1.1, but v1.0 wrote numbers, so an upgrade needs a migration and testing on real Windows.
