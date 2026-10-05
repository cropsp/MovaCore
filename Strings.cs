using System;
using System.Globalization;
using MovaCore.Models;
using MovaCore.Services;

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
        public static string TrayTooltipRecording => T("MovaCore - recording…", "MovaCore - запис…");
        public static string TrayTooltipTranscribing => T("MovaCore - transcribing…", "MovaCore - розпізнавання…");

        public static string TrayTooltipDownloading(int? percent) => percent is int p
            ? string.Format(T("MovaCore - downloading the speech model: {0}%", "MovaCore - завантаження моделі: {0}%"), p)
            : T("MovaCore - downloading the speech model", "MovaCore - завантаження моделі");
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

        public static string BalloonModelReady(string hotkey) => string.Format(T(
            "The speech model is ready. Hold {0} and speak.",
            "Модель розпізнавання готова. Утримуйте {0} і говоріть."), hotkey);

        public static string BalloonModelDownloadFailed(string reason) => string.Format(T(
            "Could not download the speech model: {0}. MovaCore will continue the download next time.",
            "Не вдалося завантажити модель розпізнавання: {0}. MovaCore продовжить завантаження наступного разу."), reason);

        // Dictation: shown in the recording indicator, or in a balloon when the indicator is off
        public static string OverlayNoSpeech => T("No speech heard", "Голосу не чути");
        public static string OverlayNoSignal => T("The microphone is silent — check it in Settings", "Мікрофон мовчить — перевірте його в налаштуваннях");

        public static string SpeechModelStillDownloading(int? percent) => percent is int p
            ? string.Format(T("The speech model is still downloading: {0}%", "Модель розпізнавання ще завантажується: {0}%"), p)
            : T("The speech model is still downloading", "Модель розпізнавання ще завантажується");

        public static string SpeechErrorText(SpeechError error, string? detail) => error switch
        {
            SpeechError.ModelMissing => T(
                "The speech model is not downloaded: open Settings, Voice.",
                "Модель розпізнавання не завантажено: відкрийте Налаштування, Голос."),
            SpeechError.ModelUnsupported => T(
                "The model file is not a whisper.cpp (ggml) model.",
                "Файл моделі не є моделлю whisper.cpp (ggml)."),
            SpeechError.MicrophoneUnavailable => T("No microphone found.", "Мікрофон не знайдено."),
            SpeechError.MicrophoneBlocked => T(
                "Windows blocks the microphone: allow desktop apps to use it in Settings, Privacy, Microphone.",
                "Windows блокує мікрофон: дозвольте класичним програмам доступ до нього в Параметрах, Конфіденційність, Мікрофон."),
            SpeechError.CpuUnsupported => T(
                "This processor lacks AVX2, which speech recognition needs.",
                "Цей процесор не підтримує AVX2, потрібний для розпізнавання мовлення."),
            SpeechError.RuntimeMissing => T(
                "Speech recognition files are missing next to MovaCore.exe: extract the whole archive again.",
                "Поруч із MovaCore.exe бракує файлів розпізнавання: розпакуйте весь архів ще раз."),
            SpeechError.ClipboardFailed => T("Could not paste the recognized text.", "Не вдалося вставити розпізнаний текст."),
            _ => string.Format(T("Speech recognition failed: {0}", "Розпізнавання не вдалося: {0}"), detail ?? "?"),
        };

        public static string DownloadErrorText(ModelDownloadError error) => error switch
        {
            ModelDownloadError.Network => T("no connection", "немає з'єднання"),
            ModelDownloadError.Server => T("the server returned an error", "сервер повернув помилку"),
            ModelDownloadError.Stalled => T("the connection stalled", "з'єднання зависло"),
            ModelDownloadError.NotEnoughSpace => T("not enough disk space", "замало місця на диску"),
            ModelDownloadError.Corrupt => T("the file arrived damaged", "файл надійшов пошкодженим"),
            _ => T("the file could not be saved", "не вдалося зберегти файл"),
        };

        /// <summary>A file size for people: "874 MB", "1.6 GB" ("874 МБ", "1,6 ГБ").</summary>
        public static string FormatSize(long bytes)
        {
            double megabytes = bytes / 1_000_000.0;
            CultureInfo culture = CultureInfo.InvariantCulture;
            string number = megabytes < 1000
                ? Math.Round(megabytes).ToString("0", culture)
                : (megabytes / 1000).ToString("0.0", culture);
            if (Language == UiLanguage.Ukrainian) number = number.Replace('.', ',');
            return number + (megabytes < 1000 ? T(" MB", " МБ") : T(" GB", " ГБ"));
        }

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
        public static string TabLayout => T("Layout", "Розкладка");
        public static string TabVoice => T("Voice", "Голос");
        public static string TabGeneral => T("General", "Загальні");
        public static string HotkeyGroup => T("Hotkey", "Гаряча клавіша");
        public static string HotkeyChange => T("Change", "Змінити");
        public static string HotkeyPrompt => T("Press a key… (Esc to cancel)", "Натисніть клавішу… (Esc — скасувати)");

        public static string HotkeyUsedForConversion => T(
            "This key combination already converts the layout.",
            "Це поєднання клавіш уже конвертує розкладку.");

        public static string HotkeyUsedForDictation => T(
            "This key combination is already the dictation hotkey.",
            "Це поєднання клавіш уже є клавішею диктування.");

        // Settings form: conversion
        public static string ConversionGroup => T("Conversion", "Конвертація");
        public static string RestoreClipboard => T("Restore the clipboard after pasting", "Відновлювати буфер обміну після вставки");
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

        // Settings form: voice
        public static string SpeechEnable => T(
            "Voice input: hold the hotkey, speak, release",
            "Голосове введення: утримуйте клавішу, говоріть, відпустіть");

        public static string SpeechHotkeyGroup => T("Dictation hotkey (hold)", "Клавіша диктування (утримувати)");
        public static string SpeechModelGroup => T("Speech model", "Модель розпізнавання");
        public static string SpeechModelCustom => T("Custom file…", "Свій файл…");
        public static string SpeechModelBrowse => T("Browse…", "Огляд…");
        public static string SpeechModelDownload => T("Download", "Завантажити");
        public static string SpeechModelWhereToGet => T("Where to get models", "Де взяти моделі");
        public static string SpeechModelDelete => T("Delete", "Видалити");

        public static string SpeechModelDownloadedSize(string size) =>
            string.Format(T("✓ Downloaded ({0})", "✓ Завантажено ({0})"), size);

        public static string SpeechModelDeleteConfirm(string name, string size) => string.Format(T(
            "Delete the speech model {0} ({1}) from this computer? You can download it again later.",
            "Видалити модель розпізнавання {0} ({1}) з комп'ютера? Її можна буде завантажити знову."), name, size);

        public static string SpeechModelDeleteTurnsOffVoice => T(
            "Voice input uses this model, so it will be turned off.",
            "Голосове введення використовує цю модель, тож його буде вимкнено.");

        public static string SpeechModelDeleteFailed(string reason) =>
            string.Format(T("Could not delete the model: {0}", "Не вдалося видалити модель: {0}"), reason);

        public static string SpeechModelNotDownloaded(string size) => string.Format(T(
            "Not downloaded yet ({0}). Saving with voice input on starts the download.",
            "Ще не завантажено ({0}). Збереження з увімкненим голосовим введенням почне завантаження."), size);

        public static string SpeechModelDownloading(string received, string total, int percent) => string.Format(T(
            "Downloading: {0} of {1} ({2}%)", "Завантаження: {0} з {1} ({2}%)"), received, total, percent);

        public static string SpeechModelDownloadStarting => T("Downloading…", "Завантаження…");

        public static string SpeechModelDownloadFailed(string reason) =>
            string.Format(T("Download failed: {0}", "Не вдалося завантажити: {0}"), reason);

        /// <summary>A catalog model as listed in the settings, e.g. "Large v3 Turbo q8_0: recommended (874 MB)".</summary>
        public static string SpeechModelName(SpeechModelInfo model)
        {
            string description = model.Id switch
            {
                SpeechModelCatalog.DefaultId => T("recommended", "рекомендована"),
                "large-v3-turbo" => T("full precision, best with a graphics card", "повна точність, найкраще з відеокартою"),
                _ => T("smaller, for slower computers", "менша, для слабших комп'ютерів"),
            };
            return $"{SpeechModelShortName(model)}: {description} ({FormatSize(model.ApproximateBytes)})";
        }

        /// <summary>A catalog model's name alone, e.g. "Large v3 Turbo q8_0".</summary>
        public static string SpeechModelShortName(SpeechModelInfo model) => model.Id switch
        {
            SpeechModelCatalog.DefaultId => "Large v3 Turbo q8_0",
            "large-v3-turbo" => "Large v3 Turbo",
            _ => "Large v3 Turbo q5_0",
        };

        public static string SpeechCustomNone => T("Choose a whisper.cpp model file (.bin)", "Виберіть файл моделі whisper.cpp (.bin)");
        public static string SpeechCustomGgml => T("✓ whisper.cpp model", "✓ Модель whisper.cpp");

        public static string SpeechCustomGguf => T(
            "GGUF files are not supported: choose a ggml .bin file",
            "Файли GGUF не підтримуються: потрібен файл ggml .bin");

        public static string SpeechCustomUnknown => T("Not a whisper.cpp model", "Це не модель whisper.cpp");
        public static string SpeechCustomMissing => T("File not found", "Файл не знайдено");

        public static string SpeechModelFileFilter => T(
            "whisper.cpp models (*.bin)|*.bin|All files (*.*)|*.*",
            "Моделі whisper.cpp (*.bin)|*.bin|Усі файли (*.*)|*.*");

        public static string SpeechRecognitionGroup => T("Recognition", "Розпізнавання");
        public static string SpeechLanguageLabel => T("Speech language:", "Мова мовлення:");
        public static string SpeechMicrophoneLabel => T("Microphone:", "Мікрофон:");
        public static string SpeechMicrophoneDefault => T("As in Windows", "Як у Windows");
        public static string SpeechMicrophoneUnavailable => T("Unavailable microphone", "Недоступний мікрофон");
        public static string SpeechUseGpu => T("Use the graphics card (Vulkan)", "Використовувати відеокарту (Vulkan)");

        public static string SpeechUseGpuTooltip => T(
            "Much faster with NVIDIA, AMD or Intel graphics. A change takes effect after restarting MovaCore.",
            "Набагато швидше з відеокартами NVIDIA, AMD чи Intel. Зміна діє після перезапуску MovaCore.");

        public static string SpeechUseGpuUnavailable => T("Not available on ARM devices.", "Недоступно на пристроях ARM.");
        public static string SpeechShowOverlay => T("Show the recording indicator", "Показувати індикатор запису");

        public static string SpeechPrivacy => T(
            "Audio is processed on this computer only: it is never saved or sent anywhere. The internet is used only to download the model from huggingface.co.",
            "Звук обробляється лише на цьому комп'ютері: він не зберігається й нікуди не надсилається. Інтернет потрібен тільки для завантаження моделі з huggingface.co.");

        /// <summary>A Whisper language code as listed in the settings; language names are in their own language.</summary>
        public static string SpeechLanguageName(string code) => code switch
        {
            SpeechLanguages.Auto => T("Detect automatically", "Визначати автоматично"),
            "uk" => LanguageUkrainian,
            "en" => LanguageEnglish,
            "pl" => "Polski",
            "de" => "Deutsch",
            "fr" => "Français",
            "es" => "Español",
            "it" => "Italiano",
            "pt" => "Português",
            _ => code,
        };

        // Settings form: excluded applications
        public static string ExcludedGroup => T("Disabled in applications", "Не працювати в програмах");
        public static string ExcludedHint => T("One program per line, e.g. devenv or Code.exe", "Одна програма на рядок, наприклад devenv або Code.exe");

        // Settings form: buttons
        public static string Save => T("Save", "Зберегти");
        public static string Cancel => T("Cancel", "Скасувати");
    }
}
