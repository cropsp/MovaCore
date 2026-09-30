# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

MovaCore is a Windows-only system-tray utility that converts selected text typed in the wrong keyboard layout
(EN ↔ UA, e.g. `ghbdsn` → `привіт`) when the user presses a global hotkey (default F10). It is written in C# on
.NET 10 WinForms and shipped as a Native AOT `MovaCore.exe` plus SharpHook's native `uiohook.dll`, which Native AOT
cannot embed, so releases are zip archives. The root namespace is still `LayoutConverter.App`
(the old project name), not `MovaCore`.

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
arm64) and smoke-tested; its job summary lists the publish output sizes and the AOT/trim warnings (all currently from WinForms itself). Pushing a `v*` tag
creates a draft release with zip archives and SHA256 sums.

## Architecture

The whole app is one conversion pipeline wired through `Microsoft.Extensions.DependencyInjection` in `Program.cs`
(all singletons). Understanding it requires following these files:

1. **`Services/IHotkeyService.cs` → `HotkeyService`** runs a SharpHook `SimpleGlobalHook` on a thread-pool thread.
   It suppresses both press and release of the trigger key and raises `HotkeyTriggered` on *release*.
   Hook callbacks run synchronously on the hook thread: `e.SuppressEvent` must be set there, and handlers must return
   fast. The same class simulates Ctrl+C / Ctrl+V via SharpHook's `EventSimulator`.
2. **`TrayApplicationContext.cs`** owns the tray icon, the settings dialog and the lifetime. It forwards
   `HotkeyTriggered` to the orchestrator via `Task.Run`, so conversion never runs on the UI or hook thread.
3. **`Services/HotkeyOrchestrator.cs`** performs the clipboard round-trip: remember the clipboard sequence number →
   simulate Ctrl+C → wait (up to `CopyTimeout`, 1 s) for the sequence number to change → read text → convert →
   write clipboard → simulate Ctrl+V. It never clears the clipboard: if the number does not change, nothing was
   selected and it stops, so stale clipboard content can never be pasted. A second hotkey press while a conversion
   is running is dropped (`Interlocked` busy flag). Errors are reported through the `ConversionCompleted` event
   (despite the name, it fires only on errors), shown as a tray balloon, and written to the log.
4. **`Services/ClipboardService.cs`** talks to the clipboard with raw Win32 P/Invoke (`OpenClipboard`,
   `GlobalAlloc`, `CF_UNICODETEXT`, `GetClipboardSequenceNumber`), retrying only while another app holds the
   clipboard open, deliberately avoiding `System.Windows.Forms.Clipboard` (COM/STA issues). `TryGetTextAsync`
   returns null when there is no text; `TrySetTextAsync` returns false instead of failing silently.
5. **`Services/ILayoutConverterService.cs`** holds two one-way maps (EN→UA, UA→EN) built from the paired
   `EnKeys`/`UaKeys` strings (same physical key at the same index). The direction is chosen once per string by
   counting characters that exist in only one layout; applying a single map to the whole string keeps conversion
   reversible (`Convert(Convert(s)) == s`). The paired strings must stay the same length without duplicates; the
   static constructor throws otherwise.

Settings: `SettingsService` is *not* registered in DI; `TrayApplicationContext` creates it directly. It stores
`Models/AppSettings` as JSON in `%APPDATA%\MovaCore\settings.json` and toggles autostart via
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. `UI/SettingsForm.cs` is built in code (no designer file) and
maps WinForms `Keys` to SharpHook `KeyCode` by hand.

`Program.cs` also owns process-wide concerns: the log (`Services/AppLog.cs`, `%LOCALAPPDATA%\MovaCore\logs`, one
rotated file), handlers for unhandled exceptions, the single-instance mutex, and an up-front check that `uiohook.dll`
loads. Never log clipboard text or keystrokes. `SmokeTest.cs` counts on `AppLog.ErrorCount`, so report failures
through `AppLog.Error` rather than swallowing them.

The tray icon, exe icon and settings logo are embedded resources (`AppResources.cs`) generated from
`Resources/mouse_icon.png` by `eng/generate-icons.py`; regenerate them instead of editing the `.ico`/`.png` by hand.

Interfaces and their implementations share a file (e.g. `IHotkeyService.cs` contains `HotkeyService`).

Tests (`tests/MovaCore.Tests`, xUnit) cannot reference the WinForms app, so the csproj compiles the platform-neutral
files from `Services/` in as linked sources (and `internal` members are visible to tests). Keep WinForms/registry code
out of those files, and add a link when a new testable file appears. The app csproj excludes `tests/**` from its
default globs because the app project sits at the repository root.

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
