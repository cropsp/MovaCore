namespace MovaCore.Models
{
    /// <summary>
    /// A downloadable Whisper model. <paramref name="ApproximateBytes"/> is for display and the free-space check until
    /// the server reports the real size; <paramref name="Sha1"/> comes from whisper.cpp's model list, where known.
    /// </summary>
    public sealed record SpeechModelInfo(string Id, string FileName, long ApproximateBytes, string? Sha1);
}
