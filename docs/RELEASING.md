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

   Once code signing is set up (below), each publish job first sends `MovaCore.exe` to SignPath and waits up to an
   hour: approve the two signing requests (x64 and arm64) in SignPath. A signing request that fails or is not
   approved in time fails the run, and no release is created; re-run the failed jobs.
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

## Code signing (one-time setup)

`MovaCore.exe` is signed through the free [SignPath Foundation](https://signpath.org) program for open source
projects; [CODE_SIGNING.md](CODE_SIGNING.md) is the policy the program requires. Until it is set up, CI skips signing
and releases are unsigned (the release notes say so), and Windows SmartScreen may warn on first start. Even signed,
a new version can get a SmartScreen warning until enough people have run it.

1. Turn on two-factor authentication for GitHub. Apply at [signpath.org](https://signpath.org) (the "Apply" form for
   open source projects) with this repository's URL, and wait for the approval.
2. In SignPath, once the project is created:
   - turn on two-factor authentication for your SignPath account;
   - connect the project to GitHub as its trusted build system (SignPath's GitHub documentation);
   - check that the project slug is `MovaCore` and the release signing policy is `release-signing` (the names CI
     uses; change `.github/workflows/ci.yml` if SignPath gave others);
   - set the artifact configuration to [eng/signpath/artifact-configuration.xml](../eng/signpath/artifact-configuration.xml);
   - create a CI user with permission to submit signing requests for the policy, and an API token for it.
3. In the repository, under **Settings → Secrets and variables → Actions**, add the secret `SIGNPATH_API_TOKEN` (the
   token) and the variable `SIGNPATH_ORGANIZATION_ID` (from SignPath's organization settings). The variable turns
   signing on.
4. Remove the "Status" paragraph from `CODE_SIGNING.md` (or name the first signed version), and add a `CHANGELOG.md`
   entry saying that releases are signed.
5. Release the next version and check the run: the "Sign with SignPath" and "Check the signature" steps ran and the
   job summary names the signer; on Windows, `MovaCore.exe` → **Properties → Digital Signatures** shows SignPath
   Foundation.

CI uses `signpath/github-action-submit-signing-request@v1`; check its README for a newer major version now and then.
