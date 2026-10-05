using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Speech to text with a locally loaded model. Implementations serialize calls, keep the model loaded between calls
    /// and reload it when the options change. Failures the user can fix throw <see cref="SpeechException"/>.
    /// </summary>
    public interface ISpeechRecognizer : IDisposable
    {
        /// <summary>Loads the model ahead of time, e.g. while the user is still speaking.</summary>
        Task PreloadAsync(SpeechOptions options, CancellationToken cancellationToken);

        /// <summary>Transcribes 16 kHz mono samples and returns the recognized segments.</summary>
        Task<IReadOnlyList<string>> TranscribeAsync(float[] samples, SpeechOptions options, CancellationToken cancellationToken);

        /// <summary>Frees the model (it takes hundreds of megabytes); the next call loads it again.</summary>
        void Unload();
    }
}
