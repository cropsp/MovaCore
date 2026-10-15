namespace MovaCore.Models
{
    /// <summary>
    /// A graphics card that Vulkan lists and whisper.cpp can use. <paramref name="Position"/> is its place among such
    /// cards, in Vulkan's order: whisper.cpp's gpu_device. <paramref name="Memory"/> is its own (device-local) memory in
    /// bytes; an integrated card's is a share of the computer's memory.
    /// </summary>
    public sealed record GpuDevice(int Position, string Name, bool Discrete, ulong Memory);
}
