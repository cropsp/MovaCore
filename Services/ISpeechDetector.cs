using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>Voice activity detection: where a recording holds speech.</summary>
    public interface ISpeechDetector
    {
        /// <summary>
        /// The stretches of speech in 16 kHz mono samples, in order (empty if there is none), or null if no detector is
        /// installed. The options tell which native runtime to use, as for recognition.
        /// </summary>
        Task<IReadOnlyList<SpeechSegment>?> DetectSpeechAsync(float[] samples, SpeechOptions options, CancellationToken cancellationToken);
    }
}
