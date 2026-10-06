namespace MovaCore.Services
{
    public enum PasteResult
    {
        Pasted,

        /// <summary>The application did not read the text in time; it stays on the clipboard for Ctrl+V.</summary>
        NotObserved,

        /// <summary>The text could not be put on the clipboard.</summary>
        ClipboardFailed,

        /// <summary>A conversion kept the clipboard for too long; nothing was pasted.</summary>
        Busy,
    }
}
