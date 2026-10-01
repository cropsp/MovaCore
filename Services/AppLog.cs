using System;
using System.Threading;

namespace MovaCore.Services
{
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
