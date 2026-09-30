# MovaCore 🐭

<p align="center">
  <img src="Resources/logo.png" width="128" alt="MovaCore Mascot">
</p>

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)

**MovaCore** is a lightweight, blazing-fast, and "polite" keyboard layout converter for Windows. 
Born from the need to seamlessly switch text between English and Ukrainian (and vice versa) without the mess of standard clipboard tools.

---

## ✨ Features

- **Blazing Fast:** Built with **.NET 10 & Native AOT**, ensuring instant startup and no .NET installation required.
- **"Polite" Clipboard Handling:** Uses raw **Win32 P/Invoke** instead of high-level wrappers, overcoming common "Access Denied" or "COM Interop" issues.
- **Smart Retries:** Automatically handles clipboard locks from other applications (like Telegram or Browsers).
- **Architecture:** 
  - Non-blocking keyboard hooks via **SharpHook**.
  - Background task orchestration to ensure your input never lags.
- **Minimalist UI:** Sits quietly in your system tray with a cute field mouse mascot.

---

## 🚀 Getting Started

### Hotkeys
- **F10 (default, fires on key release):** Highlight text and tap F10 to convert it to the other layout (e.g., `ghbdsn` -> `привіт`, `Руддщ` -> `Hello`). The hotkey can be changed in **Settings** (right-click the tray icon).

### Installation
1. Download the latest `MovaCore-<version>-win-x64.zip` (or `win-arm64` for ARM devices) from the [Releases](https://github.com/cropsp/MovaCore/releases) page.
2. Extract it and run `MovaCore.exe` — no installation or administrator rights required. Keep `uiohook.dll` next to the exe: it is the native keyboard hook. Only one instance runs at a time.
3. Find the mouse icon in your system tray.

> **Note:** Windows does not let a regular app read keystrokes from, or send input to, windows running as administrator.
> To convert text in elevated apps, start MovaCore as administrator too. "Launch at Windows startup" always starts it without elevation.

---

## 🛠 Tech Stack
- **Runtime:** .NET 10 (Native AOT)
- **Hooks:** [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- **Core Logic:** Win32 P/Invoke for Clipboard management.
- **UI:** WinForms (System Tray)

---

## 🐭 Why the Mouse?
Like a field mouse, **MovaCore** is small, quiet, and very fast at moving "seeds" (your text) from one place to another.

---

## 📜 License
Published under the [MIT License](LICENSE).

---

Developed with ❤️ and AI pairing.
