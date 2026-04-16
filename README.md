# MovaCore

**MovaCore** is a modern Windows application built with .NET 8 and WinUI 3 that allows you to instantly convert the keyboard layout of your text globally.

## 🚀 Features

- **Global Layout Conversion**: Quickly fix text typed in the wrong layout (e.g., "ghbdtn" -> "привіт") anywhere in Windows.
- **Global Hotkey**: Uses `Alt + Q` to trigger the conversion on selected text.
- **System Tray Integration**: Runs minimized in the tray to stay out of your way.
- **Modern Fluent UI**: Leverages the latest Windows App SDK for a premium look and feel.
- **MVVM Architecture**: Clean and maintainable codebase using CommunityToolkit.Mvvm.

## 🛠 Tech Stack

- **Framework**: .NET 8 / WinUI 3 (Windows App SDK)
- **Hooks**: SharpHook for global key monitoring.
- **UI Components**: H.NotifyIcon for tray integration.
- **Architecture**: MVVM (Community Toolkit).

## 📖 How to Use

1. Run the application.
2. Select text that was typed in the wrong layout.
3. Press `Alt + Q`.
4. The text will be replaced with the converted version automatically.

## 📝 License

This project is licensed under the MIT License.
