using System;

namespace MovaCore.Services
{
    public sealed class SpeechException : Exception
    {
        public SpeechException(SpeechError error, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        public SpeechError Error { get; }
    }
}
