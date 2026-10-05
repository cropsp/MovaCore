using System;

namespace MovaCore.Services
{
    /// <summary>
    /// A dictation state change. <see cref="Outcome"/> is set when the state returns to Idle; <see cref="Error"/> and
    /// <see cref="Detail"/> (a technical message, never recognized text) only when it failed.
    /// </summary>
    public sealed class SpeechStateChangedEventArgs : EventArgs
    {
        public SpeechStateChangedEventArgs(SpeechState state, SpeechOutcome? outcome = null, SpeechError? error = null, string? detail = null)
        {
            State = state;
            Outcome = outcome;
            Error = error;
            Detail = detail;
        }

        public SpeechState State { get; }
        public SpeechOutcome? Outcome { get; }
        public SpeechError? Error { get; }
        public string? Detail { get; }
    }
}
