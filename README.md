# MovaCore 🐭

<p align="center">
  <img src="Resources/logo.png" width="128" alt="MovaCore Mascot">
</p>

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-blue.svg)](https://www.microsoft.com/windows)

**MovaCore** is a lightweight, blazing-fast, and "polite" keyboard layout converter for Windows. 
Born from the need to seamlessly switch text between English and Ukrainian (and vice versa) without the mess of standard clipboard tools.
It also types what you say: hold a key, speak, and the text appears where you are typing, recognized on your PC by Whisper.

---

## ✨ Features

- **Blazing Fast:** Built with **.NET 10 & Native AOT**, ensuring instant startup and no .NET installation required.
- **"Polite" Clipboard Handling:** Uses raw **Win32 P/Invoke** instead of high-level wrappers, overcoming common "Access Denied" or "COM Interop" issues.
- **Smart Retries:** Automatically handles clipboard locks from other applications (like Telegram or Browsers).
- **Keeps Your Clipboard:** After converting, MovaCore puts back whatever you had copied before, and its own clipboard writes stay out of Windows clipboard history (Win+V). This can be turned off in Settings.
- **Architecture:** 
  - Non-blocking keyboard hooks via **SharpHook**.
  - Background task orchestration to ensure your input never lags.
- **Press Again to Undo:** The converted text stays selected, so a second press converts it back, and the keyboard layout switches to the language you meant to type in.
- **Your Layouts:** Conversion follows the layouts installed in Windows, including "Ukrainian (Enhanced)" with ґ.
- **Stays Out of the Way:** Pause it from the tray, or list applications (e.g. `devenv`, `Code`) in which the hotkey is left to the application.
- **Voice Input:** Hold a hotkey, speak, release: [Whisper](https://github.com/ggml-org/whisper.cpp) (Large v3 Turbo) recognizes the speech on your PC and pastes the text into the focused application. Choose the language and the microphone; a graphics card speeds it up (Vulkan).
- **Minimalist UI:** Sits quietly in your system tray with a cute field mouse mascot. English and Ukrainian.

---

## 🚀 Getting Started

### Hotkeys
- **F10 (default, fires on key release):** Highlight text and tap F10 to convert it to the other layout (e.g., `ghbdsn` -> `привіт`, `Руддщ` -> `Hello`). Tap it again to convert back.
- Change the hotkey in **Settings** (right-click or double-click the tray icon): press the new key, optionally with Ctrl, Shift, Alt or Win.
- In terminals, choose **Ctrl+Insert / Shift+Insert** in Settings: there Ctrl+C without a selection would interrupt the running program.

### Voice input
1. In **Settings → Voice**, turn on voice input and press **Save**. MovaCore downloads the speech model in the background (Whisper Large v3 Turbo q8_0, 874 MB, from [whisper.cpp's repository on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp)); the tray icon's tooltip shows the progress. An interrupted download continues at the next start.
2. **Hold ScrollLock** (default; change it in Settings), speak, and release it. The recognized text is pasted where the cursor is, and your clipboard is put back.
3. While you speak, the tray icon shows a red dot, and a small indicator at the bottom of the screen shows a field mouse in grass that grows with your voice. If the grass is grey and the mouse dozes, the microphone is still starting: wait until the grass turns green. If it says no speech was heard, check the microphone chosen in Settings → Voice and its input level in the Windows sound settings.

On the Voice tab you can also pick the full-precision model (1.6 GB) or a smaller one (574 MB) for slower computers, or your own whisper.cpp model file (`ggml-*.bin`; GGUF files are not supported), the speech language (automatic detection, Ukrainian, English and more) and the microphone. A downloaded model can be deleted there to free disk space.

Dictating again right after a phrase, without typing or clicking in between, continues it: MovaCore adds the space, and starts with a small letter if the sentence was not finished. After you type or click, it cannot know what is before the cursor, so the phrase is pasted as Whisper wrote it.
Recognition runs on the graphics card through Vulkan where available (x64), which is much faster, otherwise on the processor (x64 processors need AVX2, i.e. 2013 or newer).

The microphone stays open for 30 seconds after each dictation, so that the next one starts instantly: Windows shows it as in use meanwhile, and a Bluetooth headset stays in its (lower-quality) headset mode.

Audio is processed on your PC only: it is never saved or sent anywhere. The only network connection MovaCore makes is the model download from huggingface.co, after you turn voice input on.

Settings are stored in `%APPDATA%\MovaCore\settings.json`, downloaded models in `%LOCALAPPDATA%\MovaCore\models`, and a diagnostic log in `%LOCALAPPDATA%\MovaCore\logs` (it never contains your text, keystrokes or what you say).

### Installation
1. Download the latest `MovaCore-<version>-win-x64.zip` (or `win-arm64` for ARM devices) from the [Releases](https://github.com/cropsp/MovaCore/releases) page.
2. Extract the whole archive into one folder and run `MovaCore.exe` — no installation or administrator rights required. Keep the other files next to the exe: `uiohook.dll` is the native keyboard hook and the `runtimes` folder holds speech recognition. Only one instance runs at a time.
3. Find the mouse icon in your system tray.

> **Note:** Windows does not let a regular app read keystrokes from, or send input to, windows running as administrator.
> To convert text in elevated apps, start MovaCore as administrator too. "Launch at Windows startup" always starts it without elevation.

---

## 🛠 Tech Stack
- **Runtime:** .NET 10 (Native AOT)
- **Hooks:** [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- **Core Logic:** Win32 P/Invoke for Clipboard management.
- **Speech:** [whisper.cpp](https://github.com/ggml-org/whisper.cpp) through [Whisper.net](https://github.com/sandrohanea/whisper.net), voice activity detection with [Silero VAD](https://github.com/snakers4/silero-vad), microphone through [NAudio](https://github.com/naudio/NAudio) (WASAPI). The idea, and many details, come from [Handy](https://github.com/cjpais/Handy).
- **UI:** WinForms (System Tray)

---

## 🐭 Why the Mouse?
Like a field mouse, **MovaCore** is small, quiet, and very fast at moving "seeds" (your text) from one place to another.

---

## 📜 License
Published under the [MIT License](LICENSE).

---

Developed with ❤️ and AI pairing.
