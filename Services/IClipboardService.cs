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
        /// Returns false if the clipboard stays locked by another application or Windows rejects the data.
        /// </summary>
        Task<bool> TrySetTextAsync(string text);
    }
}
