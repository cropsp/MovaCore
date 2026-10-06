using System;

namespace MovaCore.Services
{
    /// <summary>
    /// The equalizer of the recording indicator: levels of voice frequency bands in the latest audio, smoothed over
    /// time. The algorithm and its calibration follow Handy's (MIT), whose bars users know: a Hann-windowed FFT,
    /// 16 bands from 400 to 4000 Hz spaced quadratically, and a decibel range measured against real dictation, so that
    /// quiet microphones still move the bars while a quiet room does not.
    /// </summary>
    public sealed class SpectrumAnalyzer
    {
        /// <summary>Samples per analysis: 32 ms, a power of two for the FFT.</summary>
        public const int WindowSize = 512;

        public const int Bands = 16;

        private const float MinHz = 400;
        private const float MaxHz = 4000;

        // Not dBFS: an average power per bin divided by the window size, which reads about 20 dB low for speech.
        // Dictation sits around the top of the range, room noise below its bottom.
        private const float FloorDb = -68;
        private const float CeilingDb = -30;
        private const float Gain = 1.3f;
        private const float CurvePower = 0.7f;

        // Weight of the newest frame (about 30 a second): bars rise and fall within a few frames, without jitter
        private const float NewFrameWeight = 0.3f;

        private readonly float[] _window = new float[WindowSize];
        private readonly (int Start, int End)[] _bandBins = new (int, int)[Bands];
        private readonly float[] _real = new float[WindowSize];
        private readonly float[] _imaginary = new float[WindowSize];
        private readonly float[] _frame = new float[Bands];
        private readonly float[] _levels = new float[Bands];

        public SpectrumAnalyzer()
        {
            for (int i = 0; i < WindowSize; i++)
                _window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / WindowSize));

            for (int band = 0; band < Bands; band++)
            {
                float startHz = MinHz + (MaxHz - MinHz) * MathF.Pow((float)band / Bands, 2);
                float endHz = MinHz + (MaxHz - MinHz) * MathF.Pow((float)(band + 1) / Bands, 2);
                int start = (int)(startHz * WindowSize / AudioSamples.SampleRate);
                int end = Math.Max((int)(endHz * WindowSize / AudioSamples.SampleRate), start + 1); // at least one bin
                _bandBins[band] = (Math.Min(start, WindowSize / 2), Math.Min(end, WindowSize / 2));
            }
        }

        /// <summary>The smoothed level of each band, from 0 to 1, lowest frequencies first.</summary>
        public ReadOnlySpan<float> Levels => _levels;

        /// <summary>
        /// Takes the last <see cref="WindowSize"/> samples into account (fewer are treated as preceded by silence; an
        /// empty span lets the bars fall).
        /// </summary>
        public void Update(ReadOnlySpan<float> samples)
        {
            Compute(samples, _frame);
            for (int band = 0; band < Bands; band++)
                _levels[band] = _levels[band] * (1 - NewFrameWeight) + _frame[band] * NewFrameWeight;
        }

        public void Reset() => Array.Clear(_levels);

        /// <summary>The levels of one frame, before smoothing over time.</summary>
        internal void Compute(ReadOnlySpan<float> samples, Span<float> levels)
        {
            int offset = WindowSize - Math.Min(samples.Length, WindowSize);
            ReadOnlySpan<float> recent = samples[^(WindowSize - offset)..];

            float mean = 0;
            foreach (float sample in recent) mean += sample;
            mean /= WindowSize; // the silence before counts as zeros
            for (int i = 0; i < WindowSize; i++)
            {
                _real[i] = ((i < offset ? 0 : recent[i - offset]) - mean) * _window[i];
                _imaginary[i] = 0;
            }
            Fft(_real, _imaginary);

            for (int band = 0; band < Bands; band++)
            {
                (int start, int end) = _bandBins[band];
                float power = 0;
                for (int bin = start; bin < end; bin++)
                    power += _real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin];
                power /= Math.Max(1, end - start);

                float db = power > 1e-12f ? 20 * MathF.Log10(MathF.Sqrt(power) / WindowSize) : -80;
                float normalized = Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
                levels[band] = Math.Clamp(MathF.Pow(normalized * Gain, CurvePower), 0f, 1f);
            }

            // A little of each neighbour: less jitter between adjacent bars
            for (int band = 1; band < Bands - 1; band++)
                levels[band] = levels[band] * 0.7f + levels[band - 1] * 0.15f + levels[band + 1] * 0.15f;
        }

        // In-place iterative radix-2 FFT
        private static void Fft(float[] real, float[] imaginary)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
                }
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                float angle = -2 * MathF.PI / length;
                float stepReal = MathF.Cos(angle), stepImaginary = MathF.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    float wReal = 1, wImaginary = 0;
                    for (int k = 0; k < length / 2; k++)
                    {
                        int even = start + k, odd = even + length / 2;
                        float tReal = real[odd] * wReal - imaginary[odd] * wImaginary;
                        float tImaginary = real[odd] * wImaginary + imaginary[odd] * wReal;
                        real[odd] = real[even] - tReal;
                        imaginary[odd] = imaginary[even] - tImaginary;
                        real[even] += tReal;
                        imaginary[even] += tImaginary;
                        (wReal, wImaginary) = (wReal * stepReal - wImaginary * stepImaginary, wReal * stepImaginary + wImaginary * stepReal);
                    }
                }
            }
        }
    }
}
