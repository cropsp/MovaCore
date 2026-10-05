# Releasing MovaCore

1. Set `<Version>` in `MovaCore.csproj`, and move the `[Unreleased]` entries in `CHANGELOG.md` under a new
   `## [x.y.z] - YYYY-MM-DD` heading (update the comparison links at the bottom).
2. Merge into `main` through a pull request once CI is green. CI builds, tests, AOT-publishes x64 and arm64 and
   runs the smoke test on the published x64 executable.
3. Tag the merge commit on `main` and push the tag:

   ```sh
   git tag -a vX.Y.Z -m "MovaCore X.Y.Z"
   git push origin vX.Y.Z
   ```

4. CI checks that the tag matches `<Version>`, packages `MovaCore-vX.Y.Z-win-x64.zip` and `-win-arm64.zip`
   (`MovaCore.exe`, `uiohook.dll`, the `runtimes` folder with the Whisper runtimes and their VC++ runtime, the
   `models` folder with the voice activity model, `LICENSE`, `THIRD-PARTY-NOTICES.txt`) with `SHA256SUMS.txt`, and creates a **draft** release whose notes are the version's
   `CHANGELOG.md` section plus installation instructions.
5. Before publishing the draft, try the x64 zip on a real Windows machine: extract it, convert a selection in a
   couple of applications, press the hotkey again to convert back, open Settings, and exit from the tray.
   Then dictation, which CI cannot try with a real microphone or graphics card:
   - turn voice input on: the model downloads (tray tooltip, Voice tab); exit halfway and start again: it resumes;
   - hold the dictation hotkey and speak in Notepad, a browser and Telegram: the text is pasted, the clipboard comes
     back, the indicator never takes the focus;
   - with the graphics card option on and off (restart in between; the log names the runtime);
   - a short tap pastes nothing; holding in silence says no speech was heard; quiet speech is still recognized;
   - the equalizer moves with the voice; a second dictation within 30 s starts at once (the log says the microphone
     was ready in 0 ms) and keeps a word spoken right as the key goes down; Pause while holding the hotkey cancels;
   - a GGUF file as the custom model is refused with a clear message.
6. Publish the draft.

The executables are not code-signed yet, so Windows SmartScreen may warn on first start.
