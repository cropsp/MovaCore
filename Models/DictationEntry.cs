using System;

namespace MovaCore.Models
{
    /// <summary>A dictated phrase in the history: when, what was recognized, and whether it was pasted.</summary>
    public sealed record DictationEntry(DateTime Time, string Text, bool Pasted);
}
