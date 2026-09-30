using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace LayoutConverter.App.Services
{
    /// <summary>
    /// Minimal size-capped file logger used to diagnose crashes and errors from user reports.
    /// Writes to <c>movacore.log</c>; when that file reaches the size limit it is moved to <c>movacore.1.log</c>
    /// (replacing any previous one), so at most one old file is kept.
    /// <para>
    /// PRIVACY: this app handles the user's clipboard and keystrokes. Callers must NEVER pass clipboard contents,
    /// selected text or pressed keys in a message or exception; log only facts about the app itself (start-up,
    /// versions, error types, timings). The logger does not and cannot filter this.
    /// </para>
    /// Logging never throws: I/O failures (locked file, missing directory, no permission) are silently ignored.
    /// </summary>
    public sealed class FileLog
    {
        private readonly object _lock = new();
        private readonly string _rotatedPath;
        private readonly long _maxBytes;

        /// <summary>Creates the log directory if needed (exceptions propagate to the caller).</summary>
        public FileLog(string directory, long maxBytes = 1_000_000)
        {
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "movacore.log");
            _rotatedPath = Path.Combine(directory, "movacore.1.log");
            _maxBytes = maxBytes;
        }

        public string FilePath { get; }

        public void Info(string message) => Write("INFO ", message, null);

        public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

        private void Write(string level, string message, Exception? exception)
        {
            var text = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append(message)
                .Append(Environment.NewLine);

            // Exception.ToString() already includes the type, message, stack trace and inner exceptions.
            if (exception != null)
                text.Append(exception).Append(Environment.NewLine);

            try
            {
                lock (_lock)
                {
                    var file = new FileInfo(FilePath);
                    if (file.Exists && file.Length >= _maxBytes)
                        File.Move(FilePath, _rotatedPath, overwrite: true);

                    File.AppendAllText(FilePath, text.ToString(), new UTF8Encoding(false));
                }
            }
            catch (IOException)
            {
                // Logging must never break the app.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never break the app.
            }
        }
    }

    /// <summary>
    /// Process-wide entry point for <see cref="FileLog"/>. Every call is a no-op until <see cref="Initialize"/>
    /// has succeeded. The same privacy rule applies: never log clipboard contents or keystrokes.
    /// </summary>
    public static class AppLog
    {
        private static volatile FileLog? _log;
        private static int _errorCount;

        /// <summary>Path of the current log file, or null while logging is not initialized.</summary>
        public static string? FilePath => _log?.FilePath;

        /// <summary>Errors reported since start, counted even when logging is disabled (used by the smoke test).</summary>
        public static int ErrorCount => Volatile.Read(ref _errorCount);

        /// <summary>Starts logging into <paramref name="directory"/>. Any failure leaves logging disabled.</summary>
        public static void Initialize(string directory)
        {
            try
            {
                _log = new FileLog(directory);
            }
            catch
            {
                // The log is a diagnostic aid only; the app must start even if it cannot be created.
            }
        }

        public static void Info(string message) => _log?.Info(message);

        public static void Error(string message, Exception? exception = null)
        {
            Interlocked.Increment(ref _errorCount);
            _log?.Error(message, exception);
        }
    }
}
