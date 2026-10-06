namespace MovaCore.Models
{
    /// <summary>What the recognizer needs: the model file, a Whisper language code ("uk", "en", … or "auto"), GPU use.</summary>
    public sealed record SpeechOptions(string ModelPath, string Language, bool UseGpu);
}
