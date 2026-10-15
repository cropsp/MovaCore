namespace MovaCore.Services
{
    /// <summary>What would make slow speech recognition faster (<see cref="RecognitionAdvisor"/>).</summary>
    public enum RecognitionAdvice
    {
        /// <summary>Nothing the user can change: it already runs on a graphics card, or there is none to use.</summary>
        None,

        /// <summary>Turn on the short audio context (Faster recognition of short phrases).</summary>
        FastRecognition,

        /// <summary>A graphics card is there but set aside (Processor only).</summary>
        ChooseGpu,

        /// <summary>A graphics card is chosen, but this process loaded the processor's runtime before.</summary>
        Restart,

        /// <summary>Vulkan lists no graphics card: its driver is missing, or there is none.</summary>
        InstallDriver,
    }
}
