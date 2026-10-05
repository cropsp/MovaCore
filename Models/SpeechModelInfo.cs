namespace MovaCore.Models
{
    /// <summary>
    /// A downloadable Whisper model. <paramref name="ApproximateBytes"/> is for display and the free-space check until
    /// the server reports the real size. The download is checked against <paramref name="Sha1"/> (from whisper.cpp's
    /// model list) or <paramref name="Sha256"/> where known, otherwise against the SHA-256 the server reports.
    /// </summary>
    public sealed record SpeechModelInfo(string Id, string FileName, long ApproximateBytes, string? Sha1, string? Sha256 = null);
}
