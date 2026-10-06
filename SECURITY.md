# Security policy

MovaCore watches the keyboard for its hotkeys, works with the clipboard and the microphone, and downloads speech
models, so security reports matter to us.

## Supported versions

Only the [latest release](https://github.com/cropsp/MovaCore/releases/latest) gets security fixes.

## Reporting a vulnerability

Please report vulnerabilities privately: on the repository's **Security** tab, choose
[**Report a vulnerability**](https://github.com/cropsp/MovaCore/security/advisories/new). Do not open a public issue.

Describe the problem, the MovaCore and Windows versions, and how to reproduce it. You can expect a first answer
within a week. Once a fix is released, the advisory is published, with credit to you if you wish.

Examples of what we treat as a vulnerability:

- MovaCore records, stores or sends keystrokes, clipboard contents, audio or recognized text;
- the model download can be made to fetch or keep a file that does not match its pinned hash;
- another program can make MovaCore run code, or a MovaCore file in the release archive is not what this repository
  builds.

The [code signing policy](docs/CODE_SIGNING.md) describes how releases are built and what MovaCore sends over the
network.
