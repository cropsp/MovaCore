using MovaCore.Services;

namespace MovaCore.Models
{
    /// <summary>A snapshot of the model download; <paramref name="Error"/> is set only when it failed.</summary>
    public sealed record ModelDownloadState(
        ModelDownloadStatus Status,
        string? ModelId,
        long BytesReceived,
        long? TotalBytes,
        ModelDownloadError? Error)
    {
        public static ModelDownloadState Idle { get; } = new(ModelDownloadStatus.Idle, null, 0, null, null);

        /// <summary>0 to 100, or null while the size is unknown.</summary>
        public int? Percent => TotalBytes is > 0 ? (int)(BytesReceived * 100 / TotalBytes.Value) : null;
    }
}
