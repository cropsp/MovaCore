using System;
using System.Threading.Tasks;

namespace MovaCore.Services
{
    public interface IClipboardService
    {
        /// <summary>
        /// Changes whenever any application writes to or empties the clipboard. Does not open the clipboard.
        /// </summary>
        uint GetSequenceNumber();

        /// <summary>
        /// Returns the clipboard text, or null if the clipboard holds no text or stays locked by another application.
        /// </summary>
        Task<string?> TryGetTextAsync();

        /// <summary>
        /// Copies the restorable part of the clipboard, or returns null if the clipboard stays locked or holds too
        /// much data to copy on every hotkey press.
        /// </summary>
        Task<ClipboardSnapshot?> TryCaptureAsync();

        /// <summary>
        /// Puts text on the clipboard, marked so that clipboard history, cloud sync and clipboard managers ignore it.
        /// Returns false if the clipboard stays locked by another application or Windows rejects the data.
        /// </summary>
        Task<bool> TrySetTextAsync(string text);

        /// <summary>
        /// Waits until an application reads the text from the last <see cref="TrySetTextAsync"/>, i.e. pastes it.
        /// Reads before <paramref name="sinceTimestamp"/> (a <see cref="System.Diagnostics.Stopwatch"/> timestamp taken
        /// before the paste was triggered) do not count. Returns false if nobody read it within the timeout.
        /// </summary>
        Task<bool> WaitForTextReadAsync(long sinceTimestamp, TimeSpan timeout);

        /// <summary>
        /// Puts the snapshot back, hidden from clipboard history, unless something else changed the clipboard in the
        /// meantime: it must still hold our own text, or be unchanged since <paramref name="expectedSequence"/>.
        /// </summary>
        Task<bool> TryRestoreAsync(ClipboardSnapshot snapshot, uint expectedSequence);
    }
}
