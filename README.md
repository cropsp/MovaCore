# MovaCore

**MovaCore** is a professional keyboard layout converter for Windows, built with .NET 8 and WinUI 3. It provides a seamless, global utility to fix text typed in the wrong layout with a single shortcut.

## ✨ Features

- **Global Conversion**: Instantly fix text like "ghbdtn" into "привіт" anywhere in Windows.
- **Modern UI**: Features a sleek, Fluent design with a **Mica backdrop** effect.
- **System Tray Integration**: Stays active in the background via the system tray, keeping your taskbar clean.
- **Responsive Shortcut**: Triggers via `Alt + Q` for instant correction of selected text.

## 🛠 How It Works

1. **Detection**: Select text in any application (Word, Browser, IDE, etc.).
2. **Action**: Press `Alt + Q`.
3. **Internal Process**:
   - The app simulates `Ctrl+C` to grab the text.
   - It performs the layout conversion (En <-> Ua).
   - It puts the new text back into the clipboard.
   - It simulates `Ctrl+V` to replace the original text.

## 📦 Installation & Build

### Requirements
- **Windows 10/11**
- **.NET 8 SDK** (for building)

### Building from Source
To build a standalone (self-contained) version that does not require the .NET Runtime on the target machine:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

## 📜 Credits

This project leverages the following open-source libraries:
- [SharpHook](https://github.com/curiosity-ai/sharphook) - For global keyboard hooks and input simulation.
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) - For advanced WinUI 3 system tray integration.
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - For the MVVM architectural pattern.

## 📝 License

This project is licensed under the MIT License.
