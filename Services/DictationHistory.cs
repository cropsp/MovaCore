using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// The last dictated phrases, newest first, so that a phrase that was not pasted (or is wanted again) can be copied
    /// from the tray menu. Kept in memory, and in a file on this computer only while the user has asked for that
    /// (<see cref="SetSaveToDisk"/>); turning it off deletes the file. Never in the log. Thread-safe: phrases arrive on
    /// worker threads, the menu reads them on the UI thread.
    /// </summary>
    public sealed class DictationHistory
    {
        public const int Capacity = 10;

        private readonly object _lock = new();
        private readonly List<DictationEntry> _entries = new(); // newest first
        private readonly string _filePath;
        private bool _saveToDisk;

        /// <param name="filePath">Where the phrases are kept while saving to disk is on.</param>
        public DictationHistory(string filePath)
        {
            _filePath = filePath;
        }

        /// <summary>The phrases, newest first.</summary>
        public IReadOnlyList<DictationEntry> Entries
        {
            get
            {
                lock (_lock) return _entries.ToArray();
            }
        }

        /// <summary>
        /// On: every phrase is written to the file, and a session that has none yet (MovaCore has just started) starts
        /// with the file's. Off: the file is deleted.
        /// </summary>
        public void SetSaveToDisk(bool saveToDisk)
        {
            lock (_lock)
            {
                bool turnedOn = saveToDisk && !_saveToDisk;
                _saveToDisk = saveToDisk;
                if (!saveToDisk)
                {
                    DeleteFile();
                }
                else if (turnedOn)
                {
                    if (_entries.Count == 0) ReadFile();
                    else WriteFile();
                }
            }
        }

        public void Add(string text, bool pasted, DateTime time)
        {
            lock (_lock)
            {
                _entries.Insert(0, new DictationEntry(time, text, pasted));
                if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
                if (_saveToDisk) WriteFile();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                DeleteFile();
            }
        }

        // The caller holds _lock
        private void ReadFile()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                List<DictationEntry>? entries =
                    JsonSerializer.Deserialize(File.ReadAllText(_filePath), SettingsJsonContext.Default.ListDictationEntry);
                if (entries == null) return;
                foreach (DictationEntry entry in entries)
                {
                    if (_entries.Count == Capacity) break;
                    if (!string.IsNullOrWhiteSpace(entry.Text)) _entries.Add(entry);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Error("Could not read the dictation history; it starts empty", ex);
            }
        }

        // Written next to the file and swapped in, so that a crash never leaves half of it
        private void WriteFile()
        {
            string tempPath = _filePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_entries, SettingsJsonContext.Default.ListDictationEntry));
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Could not save the dictation history", ex);
            }
        }

        private void DeleteFile()
        {
            try
            {
                File.Delete(_filePath);
                File.Delete(_filePath + ".tmp");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Could not delete the dictation history", ex);
            }
        }
    }
}
