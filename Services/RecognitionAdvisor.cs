namespace MovaCore.Services
{
    /// <summary>Picks the advice for slow speech recognition, the one that helps most first.</summary>
    public static class RecognitionAdvisor
    {
        /// <param name="fastRecognition">The short audio context is on.</param>
        /// <param name="useGpu">A graphics card is chosen (automatically or by name), not Processor only.</param>
        /// <param name="gpuCount">The graphics cards Vulkan lists.</param>
        /// <param name="processorRuntimeLoaded">
        /// Speech recognition runs on the processor's runtime, which stays for the process: a card needs a restart.
        /// </param>
        /// <param name="gpuSupported">The graphics card can be used at all (not on ARM: there is no Vulkan build).</param>
        public static RecognitionAdvice Choose(
            bool fastRecognition, bool useGpu, int gpuCount, bool processorRuntimeLoaded, bool gpuSupported)
        {
            if (!fastRecognition) return RecognitionAdvice.FastRecognition;
            if (!gpuSupported) return RecognitionAdvice.None;
            if (gpuCount == 0) return RecognitionAdvice.InstallDriver;
            if (!useGpu) return RecognitionAdvice.ChooseGpu;
            return processorRuntimeLoaded ? RecognitionAdvice.Restart : RecognitionAdvice.None;
        }
    }
}
