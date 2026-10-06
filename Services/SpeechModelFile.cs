using System;
using System.IO;
using MovaCore.Models;

namespace MovaCore.Services
{
    public static class SpeechModelFile
    {
        /// <summary>Tells the model format from the first four bytes of the file.</summary>
        public static SpeechModelFormat Check(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return SpeechModelFormat.Missing;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Span<byte> magic = stackalloc byte[4];
                if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length)
                    return SpeechModelFormat.Unknown;

                // whisper.cpp reads the magic as a little-endian uint32 and expects 0x67676d6c ("ggml")
                if (magic.SequenceEqual("lmgg"u8)) return SpeechModelFormat.Ggml;
                if (magic.SequenceEqual("GGUF"u8)) return SpeechModelFormat.Gguf;
                return SpeechModelFormat.Unknown;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return SpeechModelFormat.Unknown;
            }
        }
    }
}
