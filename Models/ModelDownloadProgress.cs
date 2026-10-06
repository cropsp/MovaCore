namespace MovaCore.Models
{
    /// <summary>Bytes of the model file on disk so far; the total is null while the server has not reported it.</summary>
    public readonly record struct ModelDownloadProgress(long BytesReceived, long? TotalBytes);
}
