# MovaCore

**MovaCore** is a high-performance, ultra-lightweight keyboard layout converter for Windows, built on **.NET 8** using **Windows Forms** and optimized with **Native AOT**.

## ✨ Features

- **Global Conversion**: Instantly fix text typed in the wrong layout (e.g., "ghbdtn" -> "привіт") anywhere in Windows.
- **Smart Clipboard Polling**: Advanced synchronization logic ensures text is converted even in slow-to-respond applications.
- **Ultra-Lightweight**: Native AOT compilation results in a single, standalone executable (~10 MB) with minimal memory footprint.
- **Tray-First Design**: Runs silently in the system tray. Use the context menu to manage settings or exit.
- **Global Hotkey**: Fast and reliable `Alt + Q` shortcut for instant correction.

## 🛠 Tech Stack

- **Framework**: .NET 8 (Windows Forms).
- **Optimization**: Native AOT (Ahead-of-Time compilation) for near-native performance and small size.
- **Hooks**: SharpHook for global key monitoring.
- **Architecture**: ApplicationContext-based tray lifecycle.

## 📦 Build & Publish

To build a standalone, Native AOT executable (no .NET runtime required on target):

```powershell
./publish.ps1
```

Or manually:
```bash
dotnet publish -c Release -r win-x64
```

The output will be a single `MovaCore.exe` in the `publish` directory.

## 📖 How to Use

1. Launch `MovaCore.exe`.
2. Look for the application icon in the system tray.
3. Select any text typed in the wrong layout.
4. Press **Alt + Q** to fix it instantly.

## 📜 Credits

- [SharpHook](https://github.com/curiosity-ai/sharphook) - Global keyboard hooks.
- .NET Team - Native AOT and WinForms performance.
