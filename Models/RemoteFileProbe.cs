namespace MovaCore.Models
{
    /// <summary>The first bytes of a remote file and what the server says about the whole file.</summary>
    public sealed record RemoteFileProbe(byte[] Prefix, string? Sha256, long? Size);
}
