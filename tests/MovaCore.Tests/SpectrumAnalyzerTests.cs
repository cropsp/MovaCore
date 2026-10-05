using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class SpectrumAnalyzerTests
    {
        private static float[] Tone(float hz, float amplitude, int length = SpectrumAnalyzer.WindowSize)
        {
            var samples = new float[length];
            for (int i = 0; i < length; i++)
                samples[i] = amplitude * MathF.Sin(2 * MathF.PI * hz * i / AudioSamples.SampleRate);
            return samples;
        }

        private static float[] Frame(float[] samples)
        {
            var levels = new float[SpectrumAnalyzer.Bands];
            new SpectrumAnalyzer().Compute(samples, levels);
            return levels;
        }

        [Fact]
        public void Silence_ShowsNothing()
        {
            Assert.All(Frame(new float[SpectrumAnalyzer.WindowSize]), level => Assert.Equal(0f, level));
            Assert.All(Frame(Array.Empty<float>()), level => Assert.Equal(0f, level));
        }

        [Fact]
        public void Tone_LightsUpItsBand()
        {
            float[] levels = Frame(Tone(1000, 0.1f));

            int loudest = Array.IndexOf(levels, levels.Max());
            Assert.InRange(loudest, 5, 7); // 1 kHz lies in the seventh band (906-1089 Hz)
            Assert.True(levels[loudest] > 0.6f, $"level {levels[loudest]}"); // a little less after mixing in its neighbours
            Assert.True(levels[^1] < 0.2f, $"top band {levels[^1]}");
        }

        [Fact]
        public void LowerTone_LightsUpALowerBand()
        {
            float[] low = Frame(Tone(500, 0.1f));
            float[] high = Frame(Tone(3000, 0.1f));

            Assert.True(Array.IndexOf(low, low.Max()) < Array.IndexOf(high, high.Max()));
        }

        // An audio interface with little gain: speech peaking at -40 dBFS still moves the bars
        [Fact]
        public void QuietSpeech_IsVisible()
        {
            Assert.True(Frame(Tone(1000, 0.01f)).Max() > 0.3f);
        }

        // Room noise around -70 dBFS keeps the bars down
        [Fact]
        public void RoomNoise_StaysLow()
        {
            var random = new Random(3);
            float[] noise = Enumerable.Range(0, SpectrumAnalyzer.WindowSize)
                .Select(_ => (float)(random.NextDouble() - 0.5) * 0.001f).ToArray();

            Assert.True(Frame(noise).Max() < 0.15f);
        }

        [Fact]
        public void Update_RisesAndFallsOverAFewFrames()
        {
            var analyzer = new SpectrumAnalyzer();
            float[] tone = Tone(1000, 0.1f);

            analyzer.Update(tone);
            float first = analyzer.Levels.ToArray().Max();
            for (int i = 0; i < 10; i++) analyzer.Update(tone);
            float steady = analyzer.Levels.ToArray().Max();
            for (int i = 0; i < 10; i++) analyzer.Update(ReadOnlySpan<float>.Empty);
            float fallen = analyzer.Levels.ToArray().Max();

            Assert.InRange(first, 0.1f, steady * 0.5f);
            Assert.True(steady > 0.6f);
            Assert.True(fallen < steady * 0.05f);

            analyzer.Reset();
            Assert.All(analyzer.Levels.ToArray(), level => Assert.Equal(0f, level));
        }

        // Only the latest window counts: an older loud part does not
        [Fact]
        public void LongerInput_UsesTheLatestSamples()
        {
            float[] samples = new float[SpectrumAnalyzer.WindowSize * 3];
            Tone(1000, 0.5f, SpectrumAnalyzer.WindowSize).CopyTo(samples, 0);

            Assert.All(Frame(samples), level => Assert.Equal(0f, level));
        }
    }
}
