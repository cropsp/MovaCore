# Contributing to MovaCore

Thank you for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting a bug

[Open an issue](https://github.com/cropsp/MovaCore/issues/new/choose) with the bug report form. It asks for the
MovaCore and Windows versions and the steps that lead to the problem. A few lines of the log help a lot: it is in
`%LOCALAPPDATA%\MovaCore\logs` and never contains the text you convert, your keystrokes or what you dictate. Even
so, read it before you paste it, and do not paste personal text.

Security problems are reported privately instead: see the [security policy](SECURITY.md).

## Building and testing

You need the .NET 10 SDK (`global.json` pins it). The app builds and runs on Windows only; the unit tests run on any
OS.

```powershell
dotnet build MovaCore.csproj                             # debug build
dotnet test tests/MovaCore.Tests/MovaCore.Tests.csproj   # unit tests
dotnet format MovaCore.sln --verify-no-changes           # formatting, as checked by CI
./publish.ps1                                            # Native AOT build, as released
MovaCore.exe --smoke-test                                # checks the published exe and exits
```

`CLAUDE.md` describes the architecture and the Native AOT constraints, and `docs/IMPROVEMENT_PLAN.md` (in Ukrainian)
lists known issues and the roadmap.

## Pull requests

- Keep a pull request to one change, and describe what it changes and why.
- CI must be green: formatting, the build (warnings are errors), the tests, the Native AOT publish and the smoke test
  of the published exe.
- Add a line to the `[Unreleased]` section of `CHANGELOG.md` for anything a user would notice.
- User-visible texts go into `Strings.cs` in both English and Ukrainian.
- Update `README.md` and `README.uk.md` together.
- Never log clipboard text, keystrokes, audio or recognized speech.
