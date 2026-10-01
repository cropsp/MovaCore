using System;
using MovaCore.Models;

namespace MovaCore
{
    /// <summary>
    /// Every text the user can see, in English and Ukrainian. A plain table in code instead of .resx files: with
    /// InvariantGlobalization the resource lookup could never pick a language by culture, and the app picks it from its
    /// own setting anyway. Platform-neutral on purpose, so it is also compiled into the cross-platform tests.
    /// </summary>
    internal static class Strings
    {
        /// <summary>The language in use: <see cref="UiLanguage.English"/> or <see cref="UiLanguage.Ukrainian"/>, never Auto.</summary>
        public static UiLanguage Language { get; set; } = UiLanguage.English;

        private static string T(string en, string uk) => Language == UiLanguage.Ukrainian ? uk : en;

        /// <summary>Formats an assembly version as Major.Minor.Build, e.g. 1.2.0 (a missing part counts as 0).</summary>
        public static string FormatVersion(Version? version) =>
            version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

        // Tray icon and menu
        public static string TrayTooltip => T("MovaCore - layout converter", "MovaCore - конвертер розкладки");
        public static string TrayTooltipPaused => T("MovaCore - paused", "MovaCore - на паузі");
        public static string MenuSettings => T("Settings…", "Налаштування…");
        public static string MenuPause => T("Pause", "Пауза");
        public static string MenuAbout => T("About…", "Про програму…");
        public static string MenuExit => T("Exit", "Вихід");

        // Balloons
        public static string BalloonSettingsSaved => T("Settings saved and applied.", "Налаштування збережено та застосовано.");

        public static string BalloonClipboardWriteFailed => T(
            "Could not put the converted text on the clipboard. Please try again.",
            "Не вдалося помістити конвертований текст у буфер обміну. Спробуйте ще раз.");

        public static string BalloonHookFailed(string reason) => string.Format(T(
            "The keyboard hook stopped, so the hotkey does not work. Please restart MovaCore. ({0})",
            "Перехоплення клавіатури зупинилося, тому гаряча клавіша не працює. Перезапустіть MovaCore. ({0})"), reason);

        public static string BalloonUnexpectedError(string reason) =>
            string.Format(T("Unexpected error: {0}", "Неочікувана помилка: {0}"), reason);

        // Message boxes
        public static string SettingsNotSaved(string reason) => string.Format(T(
            "The settings are applied but could not be saved: {0}",
            "Налаштування застосовано, але не вдалося зберегти: {0}"), reason);

        public static string AboutText(string version) => string.Format(T(
            "MovaCore {0}\n\nFast keyboard layout converter for Windows: fixes text typed in the wrong layout (EN ↔ UA).\n\nMIT license.",
            "MovaCore {0}\n\nШвидкий конвертер розкладки клавіатури для Windows: виправляє текст, набраний не в тій розкладці (EN ↔ UA).\n\nЛіцензія MIT."), version);

        // Startup and crash messages (Program.cs), shown before the settings are loaded
        public static string AlreadyRunning => T(
            "MovaCore is already running. Look for the mouse icon in the system tray.",
            "MovaCore уже працює. Шукайте значок мишки в системному треї.");

        public static string HookLibraryMissing => T(
            "uiohook.dll was not found next to MovaCore.exe, so the hotkey cannot work.\n\n" +
            "Extract all files from the release archive into the same folder and start MovaCore again.",
            "Поруч із MovaCore.exe немає файлу uiohook.dll, тож гаряча клавіша не працюватиме.\n\n" +
            "Розпакуйте всі файли з архіву релізу в одну теку й запустіть MovaCore знову.");

        public static string UnexpectedErrorWithLog(string reason, string? logPath) => string.Format(T(
            "Unexpected error: {0}\n\nDetails were written to {1}.",
            "Неочікувана помилка: {0}\n\nПодробиці записано в {1}."), reason, logPath ?? T("the log", "журнал"));

        public static string AboutOpenReleasesPrompt => T("Open the releases page?", "Відкрити сторінку релізів?");

        // Settings form: window and hotkey
        public static string SettingsTitle => T("MovaCore Settings", "Налаштування MovaCore");
        public static string HotkeyGroup => T("Hotkey", "Гаряча клавіша");
        public static string HotkeyChange => T("Change", "Змінити");
        public static string HotkeyPrompt => T("Press a key… (Esc to cancel)", "Натисніть клавішу… (Esc — скасувати)");

        // Settings form: conversion
        public static string ConversionGroup => T("Conversion", "Конвертація");
        public static string RestoreClipboard => T("Restore the clipboard after conversion", "Відновлювати буфер обміну після конвертації");
        public static string SwitchLayout => T("Switch the keyboard layout after conversion", "Перемикати розкладку після конвертації");

        public static string SelectConvertedText => T(
            "Select the converted text (pressing again reverts it)",
            "Виділяти конвертований текст (повторне натискання поверне оригінал)");

        public static string ConvertLastWord => T(
            "If nothing is selected, convert the last word",
            "Якщо нічого не виділено, конвертувати останнє слово");

        public static string ConvertLastWordTooltip => T(
            "May select the wrong text in slow applications.",
            "У повільних програмах може виділити не той текст.");

        public static string CopyPasteKeysLabel => T("Copy and paste keys:", "Клавіші копіювання та вставки:");
        public static string CopyPasteCtrlCV => T("Ctrl+C / Ctrl+V", "Ctrl+C / Ctrl+V");
        public static string CopyPasteCtrlInsert => T("Ctrl+Insert / Shift+Insert", "Ctrl+Insert / Shift+Insert");

        public static string CopyPasteKeysTooltip => T(
            "Ctrl+Insert / Shift+Insert does not interrupt programs running in terminals.",
            "Ctrl+Insert / Shift+Insert не перериває програми, що працюють у терміналах.");

        // Settings form: general
        public static string GeneralGroup => T("General", "Загальні");
        public static string LaunchAtStartup => T("Launch at Windows startup", "Запускати разом із Windows");
        public static string ShowNotifications => T("Show notifications", "Показувати сповіщення");
        public static string LanguageLabel => T("Language:", "Мова:");
        public static string LanguageAuto => T("Automatic (as in Windows)", "Автоматично (як у Windows)");

        // Language names are shown in their own language, whatever the interface language is
        public static string LanguageEnglish => "English";
        public static string LanguageUkrainian => "Українська";

        // Settings form: excluded applications
        public static string ExcludedGroup => T("Disabled in applications", "Не працювати в програмах");
        public static string ExcludedHint => T("One program per line, e.g. devenv or Code.exe", "Одна програма на рядок, наприклад devenv або Code.exe");

        // Settings form: buttons
        public static string Save => T("Save", "Зберегти");
        public static string Cancel => T("Cancel", "Скасувати");
    }
}
