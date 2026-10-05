namespace MovaCore.Models
{
    /// <summary>A microphone: <paramref name="Id"/> is the Windows audio endpoint ID, which survives replugging.</summary>
    public sealed record AudioInputDevice(string Id, string Name);
}
