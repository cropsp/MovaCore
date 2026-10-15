using System;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class RecognitionAdvisorTests
    {
        [Theory]
        // The short audio context helps most, wherever recognition runs
        [InlineData(false, true, 1, false, true, RecognitionAdvice.FastRecognition)]
        [InlineData(false, false, 0, true, false, RecognitionAdvice.FastRecognition)]
        // Vulkan lists no card: a driver is missing (or there is no card)
        [InlineData(true, true, 0, true, true, RecognitionAdvice.InstallDriver)]
        [InlineData(true, false, 0, true, true, RecognitionAdvice.InstallDriver)]
        // A card is there, set aside by Processor only
        [InlineData(true, false, 2, true, true, RecognitionAdvice.ChooseGpu)]
        // Chosen after the processor's runtime was loaded (the friend's laptop): only a restart helps
        [InlineData(true, true, 2, true, true, RecognitionAdvice.Restart)]
        // Already on the card, or on ARM without a Vulkan build: nothing to advise
        [InlineData(true, true, 1, false, true, RecognitionAdvice.None)]
        [InlineData(true, true, 0, true, false, RecognitionAdvice.None)]
        public void Choose_GivesTheAdviceThatHelpsMostFirst(
            bool fastRecognition, bool useGpu, int gpuCount, bool processorRuntimeLoaded, bool gpuSupported, RecognitionAdvice expected)
        {
            Assert.Equal(expected, RecognitionAdvisor.Choose(fastRecognition, useGpu, gpuCount, processorRuntimeLoaded, gpuSupported));
        }

        [Theory]
        // The 30-s window on the friend's laptop: 2 s of speech in 16 s
        [InlineData(2.0, 16.0, true)]
        // The processor with the short audio context: about as fast as it was said
        [InlineData(3.5, 3.87, false)]
        [InlineData(6.3, 6.68, false)]
        // Long dictations may take a while without being slow
        [InlineData(20.0, 21.0, false)]
        [InlineData(20.0, 31.0, true)]
        // A short wait is never slow, however short the phrase
        [InlineData(1.0, 3.9, false)]
        public void IsSlow_WhenTheTextComesMuchLaterThanTheSpeechTook(double speech, double recognition, bool expected)
        {
            Assert.Equal(expected, SpeechOrchestrator.IsSlow(TimeSpan.FromSeconds(speech), TimeSpan.FromSeconds(recognition)));
        }
    }
}
