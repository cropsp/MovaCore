using System;

namespace MovaCore.Models
{
    /// <summary>A stretch of speech in a recording, from its start.</summary>
    public readonly record struct SpeechSegment(TimeSpan Start, TimeSpan End);
}
