namespace MovaCore.Services
{
    public enum ModelDownloadError
    {
        /// <summary>No connection, or it broke off. The partial file is kept for resuming.</summary>
        Network,

        /// <summary>The server answered with an error.</summary>
        Server,

        /// <summary>No data arrived for a while. The partial file is kept for resuming.</summary>
        Stalled,

        NotEnoughSpace,

        /// <summary>The file does not match its size, hash or format; it was deleted.</summary>
        Corrupt,

        /// <summary>The file could not be written.</summary>
        Disk,
    }
}
