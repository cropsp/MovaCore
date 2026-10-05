using System;
using System.Collections.Generic;
using MovaCore.Models;

namespace MovaCore.Services
{
    /// <summary>
    /// Records a microphone. The microphone stays open between recordings until <see cref="Close"/>, so that the next
    /// recording starts at once and can include the moment before it was asked for. One thread at a time calls
    /// <see cref="Start"/>, <see cref="Stop"/> and <see cref="Close"/>; <see cref="CopyRecent"/> may be called from any.
    /// </summary>
    public interface IAudioRecorder : IDisposable
    {
        /// <summary>The active microphones; empty if there are none or they cannot be listed.</summary>
        IReadOnlyList<AudioInputDevice> GetInputDevices();

        /// <summary>
        /// Starts recording from the microphone with this endpoint ID, or the Windows default one if it is null or no
        /// longer present. If that microphone is still open, the recording starts at once and includes the last fraction
        /// of a second (people start speaking as they press the key); otherwise opening it may take a few hundred
        /// milliseconds. Throws <see cref="SpeechException"/> if it cannot start.
        /// </summary>
        void Start(string? deviceId);

        /// <summary>Stops recording and returns the audio as 16 kHz mono samples. The microphone stays open.</summary>
        float[] Stop();

        /// <summary>Releases the microphone (Windows stops showing it as in use).</summary>
        void Close();

        bool IsOpen { get; }

        bool IsRecording { get; }

        /// <summary>
        /// Copies the latest audio into the end of <paramref name="destination"/> (zeros before it, if there is less) and
        /// returns how many samples are real audio: 0 until the open microphone has delivered anything.
        /// </summary>
        int CopyRecent(Span<float> destination);
    }
}
