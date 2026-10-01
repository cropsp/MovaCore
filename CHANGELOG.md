# Changelog

All notable changes to MovaCore are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- The v1.0.0 release shipped only `MovaCore.exe`, but the keyboard hook needs `uiohook.dll` next to it, so the hotkey
  silently did nothing. Releases are now zip archives with both files, and a missing DLL is reported at startup.
- Typing `є` in the English layout converted to `` ` `` instead of `є` (`vj'` gave `` мо` `` instead of `моє`).
- Converting twice did not return the original text for `? / " , . : ;` (e.g. `Так?` → `Nfr,` → `Такб`).
- If the clipboard was locked, stale clipboard content could be pasted over the selection.
- F13–F24 could not be recorded as the hotkey, and pressing the current hotkey while recording started a conversion.
- Switching the keyboard layout (Alt+Shift, Win+Space) while a MovaCore window was focused raised an error.
- A plain F10 hotkey also swallowed Shift+F10 and Ctrl+F10.

### Added
- The clipboard is restored after converting, once the target application has actually pasted the converted text.
  MovaCore's own clipboard writes are kept out of Windows clipboard history (Win+V) and clipboard managers.
- Hotkeys with modifiers (e.g. Ctrl+Shift+F10), recorded by pressing them.
- The keyboard layout of the target window switches to the language of the converted text.
- The converted text is selected again, so a second press converts it back.
- "Pause" in the tray menu, and a list of applications in which the hotkey is left alone.
- Ctrl+Insert / Shift+Insert as an alternative to Ctrl+C / Ctrl+V (safer in terminals).
- Optional: with nothing selected, convert the word before the caret (off by default).
- Conversion tables are read from the installed layouts, so non-US English layouts and both Ukrainian layouts work.
  The built-in tables now match "Ukrainian (Enhanced)" exactly (ґ/Ґ on the backslash key); v1.0.0 mixed it with
  the older "Ukrainian" layout.
- Ukrainian user interface, "About" with a link to the releases page, single-instance protection, a log file in
  `%LOCALAPPDATA%\MovaCore\logs`, ARM64 builds.

### Changed
- .NET 10 (LTS) instead of .NET 8, SharpHook 7.1; the executable is 18.4 MB instead of 54 MB.
- The tray icon has a transparent background and is embedded in the executable.
- Settings store the hotkey by name; files written by v1.0.0 still load.
- "Launch at Windows startup" reflects the actual autostart entry and follows the executable if it is moved.

## [1.0.0] - 2026-04-16

- First release.

[Unreleased]: https://github.com/cropsp/MovaCore/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/cropsp/MovaCore/releases/tag/v1.0.0
