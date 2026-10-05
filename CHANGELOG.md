# Changelog

All notable changes to MovaCore are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Voice input: hold the dictation hotkey (ScrollLock by default), speak and release, and the speech is recognized
  locally with Whisper and pasted into the focused application; the clipboard is restored afterwards. Settings:
  language, microphone, graphics card (Vulkan, x64) and the recording indicator.
- Turning voice input on downloads the speech model (Whisper Large v3 Turbo q8_0, 874 MB; the full-precision and a
  smaller variant can be chosen, or a whisper.cpp model file of your own) in the background, checks it against its
  hash and resumes an interrupted download at the next start. This is the only network connection MovaCore makes.
- While dictating, the tray icon shows a red dot (amber while recognizing), and a small indicator at the bottom of the
  screen shows the field mouse from the logo sitting in grass that grows with your voice, without taking the focus.
  The mouse dozes until the microphone is ready, gnaws an ear of wheat if recognition takes a moment, and winks when
  the text is pasted.
- Voice activity detection (Silero VAD, shipped in a `models` folder) decides what is speech: quiet microphones work,
  silence is not sent to Whisper (which would invent phrases), and pauses are cut out. After a deliberate hold, the
  indicator says when no speech was heard or the microphone delivered no signal.
- The microphone stays open for 30 seconds after a dictation, so the next one starts at once and keeps the moment
  before the key was pressed; recording also goes on for 150 ms after the release, so the last word is not cut off.
  Meanwhile Windows shows the microphone as in use, and a Bluetooth headset stays in headset mode.
- Hesitations ("hmm", "хм") and words Whisper repeats in a loop are removed from the text.
- A phrase dictated right after another one, into the same field and with no key pressed or click in between,
  continues it: it gets a space, and a small first letter when the previous phrase did not end a sentence.
- A downloaded speech model can be deleted on the Voice tab (its size is shown). Deleting the model voice input
  uses turns voice input off; it is not downloaded again on its own.

### Changed
- The settings window has tabs: Layout, Voice and General. Restoring the clipboard and the copy and paste keys moved
  to General, since they apply to dictation too.
- The release archives contain `runtimes` and `models` folders (speech recognition, with the Visual C++ runtime it
  needs) and `THIRD-PARTY-NOTICES.txt`; the x64 archive is larger mostly because of the Vulkan build of whisper.cpp.

## [1.1.0] - 2026-10-01

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
  `%LOCALAPPDATA%\MovaCore\logs`.
- ARM64 builds (built and packaged by CI, not yet tested on ARM hardware).

### Changed
- .NET 10 (LTS) instead of .NET 8, SharpHook 7.1; the executable is 18.4 MB instead of 54 MB.
- The tray icon has a transparent background and is embedded in the executable.
- Settings store the hotkey by name; files written by v1.0.0 still load.
- "Launch at Windows startup" reflects the actual autostart entry and follows the executable if it is moved.

## [1.0.0] - 2026-04-16

- First release.

[Unreleased]: https://github.com/cropsp/MovaCore/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/cropsp/MovaCore/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/cropsp/MovaCore/releases/tag/v1.0.0
