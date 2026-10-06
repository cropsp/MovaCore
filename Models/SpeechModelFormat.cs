namespace MovaCore.Models
{
    public enum SpeechModelFormat
    {
        /// <summary>No file at the path.</summary>
        Missing,

        /// <summary>A whisper.cpp model (legacy ggml format): the only kind that loads.</summary>
        Ggml,

        /// <summary>A GGUF file, e.g. from Handy's newer model catalog, which whisper.cpp cannot load.</summary>
        Gguf,

        /// <summary>Anything else, or a file that cannot be read.</summary>
        Unknown,
    }
}
