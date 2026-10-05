using System;

namespace MovaCore.Services
{
    public sealed class ModelDownloadException : Exception
    {
        public ModelDownloadException(ModelDownloadError error, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        public ModelDownloadError Error { get; }
    }
}
