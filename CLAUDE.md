# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

MovaCore is a Windows-only system-tray utility that converts selected text typed in the wrong keyboard layout
(EN ↔ UA, e.g. `ghbdsn` → `привіт`) when the user presses a global hotkey (default F10). It is written in C# on
.NET 10 WinForms and shipped as a Native AOT `MovaCore.exe` plus SharpHook's native `uiohook.dll`, which Native AOT
cannot embed, so releases are zip archives. The root namespace is `MovaCore`.

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
WinForms itself). Pushing a `v*` tag creates a draft release with zip archives and SHA256 sums; the tag must match
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
   in the excluded list. Hook callbacks run synchronously on the hook thread: `e.SuppressEvent` must be set there,
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
   A second hotkey press while a conversion runs is dropped (`Interlocked` busy flag). `ConversionFailed` reports
   errors (a tray balloon) in addition to the log.
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

Settings: `Services/SettingsService.cs` stores `Models/AppSettings` as JSON in `%APPDATA%\MovaCore\settings.json`
(atomic write) and never shows UI; `Save` throws and the caller reports it. The Windows autostart entry
(`HKCU\...\Run`) lives behind `IStartupRegistration` (`StartupRegistration.cs`) and is the source of truth for
`LaunchAtStartup`; `Load` re-points it at the running exe only when the registered exe no longer exists.
`UI/SettingsForm.cs` is built in code (no designer file) from auto-sizing `TableLayoutPanel`s with
`AutoScaleMode.Dpi`; it records the hotkey through `IHotkeyService.CaptureHotkeyAsync`. The tray offers Settings, Pause
(stops the hook; not persisted), About and Exit, and only one settings window at a time.

All user-visible text lives in `Strings.cs` (English and Ukrainian, chosen by `AppSettings.Language`, where `Auto`
follows the Windows display language via `UI/WindowsLanguage.cs`); an in-code table keeps it simple under AOT.
Add new texts to `Strings` in both languages (`StringsTests` checks that none is empty). Do not turn on
`InvariantGlobalization`: WinForms builds a `CultureInfo` for the keyboard layout whenever the user switches layouts
in one of our windows, and that throws in invariant mode.

The tray icon, exe icon and settings logo are embedded resources (`AppResources.cs`) generated from
`Resources/mouse_icon.png` by `eng/generate-icons.py`; regenerate them instead of editing the `.ico`/`.png` by hand.

One top-level type per file; interfaces live next to their implementations in `Services/`.

Tests (`tests/MovaCore.Tests`, xUnit) cannot reference the WinForms app, so the csproj compiles the platform-neutral
files in as linked sources (and `internal` members are visible to tests). `HotkeyService` and `ClipboardService` are
Windows-only and are not linked: tests use fakes (`tests/MovaCore.Tests/Fakes.cs`), and the real Win32 behaviour is
covered by the smoke test in CI. Keep WinForms/registry code out of linked files, and add a link when a new testable
file appears. The app csproj excludes `tests/**` from its default globs because the app project sits at the root.

## Native AOT constraints

- Anything serialized with `System.Text.Json` must go through the source-generated `SettingsJsonContext`
  (`Models/AppSettings.cs`); add new types to its `[JsonSerializable]` list. Reflection-based serialization breaks
  under AOT.
- WinForms is not officially AOT-supported: `_SuppressWinFormsTrimError` forces the publish, and ILC warnings (all
  from WinForms today) are kept non-fatal with `IlcTreatWarningsAsErrors=false`. A new IL warning naming MovaCore or
  SharpHook in the CI summary is a real problem. A clean `dotnet build` proves nothing about the published exe;
  verify UI paths against the AOT-published binary.
- WinForms and Native AOT are verified only by running the published exe (`--smoke-test`, run by CI). When you touch
  UI, resources, P/Invoke or DI registration, extend `SmokeTest.cs` if the new path is not exercised.
- SharpHook `KeyCode` values are not contiguous (e.g. `VcF12 = 0x7B` but `VcF13 = 0xF000`), so never compute key
  codes arithmetically.
- SharpHook is pinned to 7.1.x. Version 8 renumbers `KeyCode` and reworks the simulation API; settings store keys by
  name since v1.1, but v1.0 wrote numbers, so an upgrade needs a migration and testing on real Windows.
