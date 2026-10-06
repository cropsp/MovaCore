namespace MovaCore.Models
{
    /// <summary>
    /// What the recognizer needs: the model file, a Whisper language code ("uk", "en", … or "auto"), GPU use, and whether
    /// short phrases may be encoded with a shorter audio context (<see cref="Services.WhisperAudioContext"/>).
    /// </summary>
    public sealed record SpeechOptions(string ModelPath, string Language, bool UseGpu, bool FastRecognition = false);
}
