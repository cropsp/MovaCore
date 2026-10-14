namespace MovaCore.Models
{
    /// <summary>How long the speech model stays in memory after the last dictation (the choices Handy offers).</summary>
    public enum ModelUnloadTimeout
    {
        /// <summary>Loaded as long as dictation is on: no dictation ever waits for the model.</summary>
        Never,

        /// <summary>Freed after every dictation and loaded again, while the user speaks, at the next press.</summary>
        Immediately,

        After2Minutes,
        After5Minutes,
        After10Minutes,
        After15Minutes,
        After1Hour,
    }
}
