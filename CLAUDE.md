# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

MovaCore is a Windows-only system-tray utility that converts selected text typed in the wrong keyboard layout
(EN ↔ UA, e.g. `ghbdsn` → `привіт`) when the user presses a global hotkey (default F10). It is written in C# on
.NET 8 WinForms and shipped as a single Native AOT executable. The root namespace is still `LayoutConverter.App`
(the old project name), not `MovaCore`.

A prioritized review of known bugs and the roadmap lives in `docs/IMPROVEMENT_PLAN.md` (in Ukrainian); check it
before changing behaviour, since many "odd" things in the code are already catalogued there.

## Commands

The app targets `net8.0-windows10.0.17763.0` with `UseWindowsForms`; the test project targets plain `net8.0` and
runs anywhere.

```powershell
dotnet build MovaCore.csproj                           # debug build (Windows)
dotnet run --project MovaCore.csproj                   # run (appears only as a tray icon)
./publish.ps1                                          # Native AOT publish, win-x64
dotnet publish MovaCore.csproj -c Release -r win-x64   # same as publish.ps1
# output: bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/

dotnet test tests/MovaCore.Tests/MovaCore.Tests.csproj                                       # all tests
dotnet test tests/MovaCore.Tests/MovaCore.Tests.csproj --filter "FullyQualifiedName~HotkeyOrchestratorTests"  # one class
```

Compile-checking the app on Linux: with Microsoft's official SDK, add `-p:EnableWindowsTargeting=true`. Distro
source-built SDKs (e.g. Ubuntu's `dotnet-sdk-8.0`) lack the WindowsDesktop SDK and fail with `MSB4019`; for those, use
the shim in `eng/LinuxCompileCheck.targets` (command in the file). Either way the output is not runnable.
There is no linter config or CI yet.

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
   (despite the name, it fires only on errors), shown as a tray balloon.
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

Interfaces and their implementations share a file (e.g. `IHotkeyService.cs` contains `HotkeyService`).

Tests (`tests/MovaCore.Tests`, xUnit) cannot reference the WinForms app, so the csproj compiles the platform-neutral
files from `Services/` in as linked sources (and `internal` members are visible to tests). Keep WinForms/registry code
out of those files, and add a link when a new testable file appears. The app csproj excludes `tests/**` from its
default globs because the app project sits at the repository root.

## Native AOT constraints

- Anything serialized with `System.Text.Json` must go through the source-generated `SettingsJsonContext`
  (`Models/AppSettings.cs`); add new types to its `[JsonSerializable]` list. Reflection-based serialization breaks
  under AOT.
- Trim/AOT warnings are suppressed in the csproj (`SuppressTrimAnalysisWarnings`, `_SuppressWinFormsTrimError`),
  and WinForms is not officially AOT-supported. A clean `dotnet build` proves nothing about the published exe;
  verify UI paths against the AOT-published binary.
- `UseSystemResourceKeys=true` and `StackTraceSupport=false`: framework exception messages become resource keys and
  stack traces are unavailable in release builds.
- SharpHook `KeyCode` values are not contiguous (e.g. `VcF12 = 0x7B` but `VcF13 = 0xF000`), so never compute key
  codes arithmetically.
- `Resources/mouse_icon.png` is loaded from disk next to the exe at runtime (tray icon and settings logo), and the
  app falls back to a default icon when it is missing.
