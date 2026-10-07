# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

**Status:** MovaCore has applied to the SignPath Foundation program. Releases up to and including 1.2.0 are not
signed; the release notes of each version say whether its `MovaCore.exe` is.

## What is signed

- Only `MovaCore.exe`, in the x64 and arm64 release archives. It is built from this repository by
  [GitHub Actions](../.github/workflows/ci.yml) on GitHub-hosted runners, from a version tag on `main`, and the same
  run sends it to SignPath. Nothing built elsewhere, such as on a developer's computer, is signed.
- The archives also contain third-party files, which MovaCore does not sign: `uiohook.dll` (libuiohook, through
  SharpHook), the whisper.cpp libraries in `runtimes` (through Whisper.net), the Microsoft Visual C++ runtime next to
  them (signed by Microsoft) and the Silero voice activity model in `models`. Their sources and licenses are listed in
  [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt).
- Each release lists the SHA-256 checksums of its archives in `SHA256SUMS.txt`.

## Team roles

- Committers and reviewers: [@cropsp](https://github.com/cropsp)
- Approvers: [@cropsp](https://github.com/cropsp)

Every signing request is approved by hand in SignPath. Team members use multi-factor authentication for GitHub and
SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

- The only network connection MovaCore makes is downloading a speech recognition model from
  [huggingface.co](https://huggingface.co/ggerganov/whisper.cpp), after the user turns voice input on (or resumes an
  interrupted download of it). Only the model file is requested; nothing about the user, their computer or their
  speech is sent.
- Speech is recognized on the user's computer. Audio is never saved or sent anywhere, and recognized text is never
  sent anywhere: the last 10 phrases stay in memory for the tray menu, and are written to a file on the computer
  (`%LOCALAPPDATA%\MovaCore\history.json`) only while the user has turned that on; turning it off deletes the file.
- There is no telemetry. The diagnostic log in `%LOCALAPPDATA%\MovaCore\logs` stays on the computer and never contains
  the converted text, keystrokes or what was said.
