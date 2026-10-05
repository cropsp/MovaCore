using System;
using System.Collections.Generic;
using MovaCore.Models;

namespace MovaCore.Services
{
    public interface IAudioRecorder : IDisposable
    {
        /// <summary>The active microphones; empty if there are none or they cannot be listed.</summary>
        IReadOnlyList<AudioInputDevice> GetInputDevices();

        /// <summary>
        /// Starts recording from the microphone with this endpoint ID, or the Windows default one if it is null or no
        /// longer present. May take a few hundred milliseconds. Throws <see cref="SpeechException"/> if it cannot start.
        /// </summary>
        void Start(string? deviceId);

        /// <summary>Stops recording and returns the audio as 16 kHz mono samples.</summary>
        float[] Stop();

        bool IsRecording { get; }

        /// <summary>Peak level of the most recent audio, from 0 to 1 (for the recording indicator).</summary>
        float CurrentLevel { get; }
    }
}
