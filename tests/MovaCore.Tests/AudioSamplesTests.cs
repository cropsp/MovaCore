using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class AudioSamplesTests
    {
        [Fact]
        public void Duration_UsesSixteenKilohertz()
        {
            Assert.Equal(TimeSpan.FromSeconds(1.5), AudioSamples.Duration(24000));
        }

        [Fact]
        public void PeakAndRms_OfAKnownSignal()
        {
            float[] samples = { 0.5f, -0.5f, 0.5f, -0.5f };

            Assert.Equal(0.5f, AudioSamples.Peak(samples));
            Assert.Equal(0.5f, AudioSamples.Rms(samples), 5);
            Assert.Equal(0f, AudioSamples.Rms(ReadOnlySpan<float>.Empty));
        }

        [Theory]
        [InlineData(1f, 0)]
        [InlineData(0.1f, -20)]
        [InlineData(0.01f, -40)]
        [InlineData(0f, -100)]
        public void ToDecibels_IsRelativeToFullScale(float level, double expected)
        {
            Assert.Equal(expected, AudioSamples.ToDecibels(level), 3);
        }

        [Fact]
        public void Zeros_AreAllZeroAndSilent()
        {
            var zeros = new float[16000];

            Assert.True(AudioSamples.IsAllZero(zeros));
            Assert.True(AudioSamples.IsSilent(zeros));
        }

        [Fact]
        public void RoomNoise_IsSilentButNotAllZero()
        {
            var random = new Random(1);
            float[] noise = Enumerable.Range(0, 16000).Select(_ => (float)(random.NextDouble() - 0.5) * 0.004f).ToArray();

            Assert.False(AudioSamples.IsAllZero(noise));
            Assert.True(AudioSamples.IsSilent(noise));
        }

        [Fact]
        public void Speech_IsNotSilent()
        {
            Assert.False(AudioSamples.IsSilent(FakeAudioRecorder.Speech(0.5)));
        }

        // One short word in a second of silence still counts
        [Fact]
        public void ShortBurstInSilence_IsNotSilent()
        {
            var samples = new float[16000];
            FakeAudioRecorder.Speech(0.1).CopyTo(samples, 8000);

            Assert.False(AudioSamples.IsSilent(samples));
        }

        // A single click is not speech
        [Fact]
        public void SingleClick_IsSilent()
        {
            var samples = new float[16000];
            samples[8000] = 1f;

            Assert.True(AudioSamples.IsSilent(samples));
        }

        [Fact]
        public void PadToMinimum_AppendsSilence()
        {
            float[] padded = AudioSamples.PadToMinimum(new[] { 0.1f, 0.2f }, TimeSpan.FromSeconds(1.25));

            Assert.Equal(20000, padded.Length);
            Assert.Equal(0.1f, padded[0]);
            Assert.Equal(0.2f, padded[1]);
            Assert.Equal(0f, padded[^1]);
        }

        [Fact]
        public void PadToMinimum_LeavesLongAudioAlone()
        {
            float[] samples = FakeAudioRecorder.Speech(2);

            Assert.Same(samples, AudioSamples.PadToMinimum(samples, TimeSpan.FromSeconds(1.25)));
        }

        // A quiet microphone (speech around -50 dBFS) is speech for the fallback threshold
        [Fact]
        public void QuietSpeech_IsNotSilent()
        {
            float[] samples = FakeAudioRecorder.Speech(0.5);
            for (int i = 0; i < samples.Length; i++) samples[i] *= 0.015f; // peak 0.003, -50 dBFS

            Assert.False(AudioSamples.IsSilent(samples));
        }

        [Fact]
        public void KeepSegments_JoinsTheSpeech()
        {
            float[] samples = Enumerable.Range(0, 32000).Select(i => (float)i).ToArray();
            var segments = new[]
            {
                new SpeechSegment(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1)),
                new SpeechSegment(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1.75)),
            };

            float[] kept = AudioSamples.KeepSegments(samples, segments);

            Assert.Equal(8000 + 4000, kept.Length);
            Assert.Equal(8000f, kept[0]);
            Assert.Equal(15999f, kept[7999]);
            Assert.Equal(24000f, kept[8000]);
            Assert.Equal(27999f, kept[^1]);
        }

        [Fact]
        public void KeepSegments_MergesOverlapsAndClampsToTheRecording()
        {
            var samples = new float[16000];
            var segments = new[]
            {
                new SpeechSegment(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2)), // past the end
                new SpeechSegment(TimeSpan.FromSeconds(-0.1), TimeSpan.FromSeconds(0.25)), // before the start, out of order
                new SpeechSegment(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.6)), // overlaps both
            };

            Assert.Same(samples, AudioSamples.KeepSegments(samples, segments));
        }

        [Fact]
        public void KeepSegments_WithoutSegments_KeepsNothing()
        {
            Assert.Empty(AudioSamples.KeepSegments(new float[16000], Array.Empty<SpeechSegment>()));
        }
    }
}
