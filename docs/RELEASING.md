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
   (`MovaCore.exe`, `uiohook.dll`, `LICENSE`) with `SHA256SUMS.txt`, and creates a **draft** release whose notes are
   the version's `CHANGELOG.md` section plus installation instructions.
5. Before publishing the draft, try the x64 zip on a real Windows machine: extract it, convert a selection in a
   couple of applications, press the hotkey again to convert back, open Settings, and exit from the tray.
6. Publish the draft.

The executables are not code-signed yet, so Windows SmartScreen may warn on first start.
