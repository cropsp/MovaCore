using System;

namespace MovaCore.Services
{
    /// <summary>Helpers for recorded audio: 16 kHz mono float samples, the format Whisper expects.</summary>
    public static class AudioSamples
    {
        public const int SampleRate = 16000;

        // Speech is judged in 30 ms frames
        private const int FrameLength = SampleRate * 30 / 1000;

        // Loud frames needed to count as speech (90 ms): a click or a tap on the desk fills one, a short word several
        private const int SpeechFrames = 3;

        /// <summary>
        /// RMS level (about -42 dBFS) that at least one frame must reach to count as speech. Room noise through a
        /// microphone stays below it; quiet speech is well above it.
        /// </summary>
        internal const float SpeechRms = 0.008f;

        public static TimeSpan Duration(int sampleCount) => TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        public static float Peak(ReadOnlySpan<float> samples)
        {
            float peak = 0;
            foreach (float sample in samples) peak = Math.Max(peak, Math.Abs(sample));
            return peak;
        }

        public static float Rms(ReadOnlySpan<float> samples)
        {
            if (samples.IsEmpty) return 0;

            double sum = 0;
            foreach (float sample in samples) sum += (double)sample * sample;
            return (float)Math.Sqrt(sum / samples.Length);
        }

        /// <summary>A driver delivering nothing but zeros is a broken capture format, not a quiet room.</summary>
        public static bool IsAllZero(ReadOnlySpan<float> samples) => samples.IndexOfAnyExcept(0f) < 0;

        /// <summary>
        /// True if fewer than three frames are loud enough to be speech. Whisper invents phrases for silence ("Thanks
        /// for watching"), so silent recordings are not transcribed at all.
        /// </summary>
        public static bool IsSilent(ReadOnlySpan<float> samples)
        {
            int loudFrames = 0;
            for (int start = 0; start < samples.Length; start += FrameLength)
            {
                ReadOnlySpan<float> frame = samples.Slice(start, Math.Min(FrameLength, samples.Length - start));
                if (Rms(frame) >= SpeechRms && ++loudFrames >= SpeechFrames) return false;
            }
            return true;
        }

        /// <summary>Appends silence up to <paramref name="minimum"/>: whisper.cpp skips input shorter than a second.</summary>
        public static float[] PadToMinimum(float[] samples, TimeSpan minimum)
        {
            int minimumLength = (int)(minimum.TotalSeconds * SampleRate);
            if (samples.Length >= minimumLength) return samples;

            var padded = new float[minimumLength];
            samples.CopyTo(padded, 0);
            return padded;
        }
    }
}
