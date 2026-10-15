using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class WhisperAudioContextTests
    {
        private static int Samples(double seconds) => (int)(seconds * AudioSamples.SampleRate);

        [Theory]
        [InlineData(0.0, 256)]   // nothing still gets the margin
        [InlineData(1.25, 256)]  // the shortest audio the orchestrator sends
        [InlineData(4.0, 256)]   // 4 s + 1 s margin = 250 positions
        [InlineData(4.2, 512)]
        [InlineData(9.0, 512)]
        [InlineData(10.0, 768)]
        [InlineData(24.0, 1280)]
        [InlineData(25.0, 1500)] // the next step would pass the whole window
        [InlineData(120.0, 1500)]
        public void Context_CoversThePhraseWithAMargin(double seconds, int expected) =>
            Assert.Equal(expected, WhisperAudioContext.For(Samples(seconds)));

        [Fact]
        public void Context_IsAStepOrTheWholeWindow()
        {
            for (double seconds = 0; seconds <= 40; seconds += 0.1)
            {
                int context = WhisperAudioContext.For(Samples(seconds));
                Assert.True(context == WhisperAudioContext.Full || context % WhisperAudioContext.Step == 0, $"{seconds} s: {context}");
                Assert.InRange(context, WhisperAudioContext.Step, WhisperAudioContext.Full);
                // The phrase itself always fits: 50 positions a second
                Assert.True(context >= System.Math.Min(seconds * 50, WhisperAudioContext.Full), $"{seconds} s: {context}");
            }
        }
    }
}
