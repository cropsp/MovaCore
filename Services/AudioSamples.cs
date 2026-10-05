using System;
using System.Collections.Generic;
using MovaCore.Models;

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
        /// RMS level (about -56 dBFS) that a frame must reach to count as speech when no voice activity detector is
        /// installed. Low on purpose: a quiet microphone or audio interface easily keeps speech below -40 dBFS, and
        /// throwing away what was said is worse than transcribing a quiet room.
        /// </summary>
        internal const float SpeechRms = 0.0015f;

        /// <summary>
        /// Peak level (-60 dBFS) below which a recording has no usable signal at all: a muted microphone, a wrong input
        /// or channel. Speech through any working microphone peaks far above it.
        /// </summary>
        public const float SignalPeak = 0.001f;

        public static TimeSpan Duration(int sampleCount) => TimeSpan.FromSeconds((double)sampleCount / SampleRate);

        /// <summary>A level from 0 to 1 in dBFS, floored at -100 (for the log and the level meter).</summary>
        public static double ToDecibels(float level) => level <= 1e-5f ? -100 : 20 * Math.Log10(level);

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
        /// True if fewer than three frames are loud enough to be speech (the fallback when no voice activity detector
        /// is installed). Whisper invents phrases for silence ("Thanks for watching"), so silent recordings are not
        /// transcribed at all.
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

        /// <summary>
        /// Only the given stretches of speech, joined: silence before, after and between them goes (overlapping or
        /// touching stretches merge). Whisper is faster and invents less without long silences.
        /// </summary>
        public static float[] KeepSegments(float[] samples, IReadOnlyList<SpeechSegment> segments)
        {
            var ranges = new List<(int Start, int End)>(segments.Count);
            foreach (SpeechSegment segment in segments)
            {
                int start = Math.Clamp((int)(segment.Start.TotalSeconds * SampleRate), 0, samples.Length);
                int end = Math.Clamp((int)Math.Ceiling(segment.End.TotalSeconds * SampleRate), 0, samples.Length);
                if (end > start) ranges.Add((start, end));
            }
            ranges.Sort();

            var merged = new List<(int Start, int End)>(ranges.Count);
            foreach ((int start, int end) in ranges)
            {
                if (merged.Count > 0 && start <= merged[^1].End)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
                else
                    merged.Add((start, end));
            }
            if (merged.Count == 1 && merged[0] == (0, samples.Length)) return samples;

            int length = 0;
            foreach ((int start, int end) in merged) length += end - start;
            var result = new float[length];
            int position = 0;
            foreach ((int start, int end) in merged)
            {
                samples.AsSpan(start, end - start).CopyTo(result.AsSpan(position));
                position += end - start;
            }
            return result;
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
