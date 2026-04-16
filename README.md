# MovaCore

**MovaCore** is a professional keyboard layout converter for Windows, built with .NET 8 and WinUI 3. It provides a seamless, global utility to fix text typed in the wrong layout with a single shortcut.

## ✨ Features

- **Global Conversion**: Instantly fix text like "ghbdtn" into "привіт" anywhere in Windows.
- **Modern UI**: Features a sleek, Fluent design with a **Mica backdrop** effect.
- **Unpackaged Distribution**: Runs as a standard standalone `.exe` without requiring Microsoft Store installation.
- **System Tray Integration**: Stays active in the background via the system tray, keeping your taskbar clean.
- **Responsive Shortcut**: Triggers via `Alt + Q` for instant correction of selected text.

## 🛠 How It Works

1. **Detection**: Select text in any application (Word, Browser, IDE, etc.).
2. **Action**: Press `Alt + Q`.
3. **Internal Process**:
   - The app simulates `Ctrl+C` to grab the text.
   - It performs the layout conversion (En <-> Ua) on the UI thread.
   - It puts the new text back into the clipboard.
   - It simulates `Ctrl+V` to replace the original text.

## 📦 Installation & Build

### Requirements
- **Windows 10/11**
- **.NET 8 SDK** (for building)

### Building a Standalone EXE
This project is configured as an **Unpackaged** WinUI 3 application. To build a self-contained version that doesn't require the .NET Runtime on the target machine, run the following command:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

**Note on Distribution**: Due to WinUI 3 architecture, the resulting `.exe` depends on several companion files (like `resources.pri` and DLLs). For distribution, it is recommended to **ZIP the entire publish folder** and provide it to users.

## 📜 Credits

This project leverages the following open-source libraries:
- [SharpHook](https://github.com/curiosity-ai/sharphook) - For global keyboard hooks and input simulation.
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) - For advanced WinUI 3 system tray integration.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - For the MVVM architectural pattern.

## 📝 License

This project is licensed under the MIT License.
