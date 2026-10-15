using System;

namespace MovaCore.Services
{
    /// <summary>
    /// How much of Whisper's 30-second window the encoder computes. Whisper encodes 30 s of audio (1500 positions of
    /// 20 ms) whatever the phrase's length, padding it with silence, so a 2-second phrase costs as much as a 30-second
    /// one, and on a weak computer that takes many seconds. whisper.cpp can encode just the start of the window instead
    /// (audio_ctx): short phrases get several times faster, at some risk to accuracy. Sizes come in steps, so that a
    /// processor built for one phrase serves the next ones of similar length.
    /// </summary>
    public static class WhisperAudioContext
    {
        /// <summary>The whole window: what Whisper was trained on.</summary>
        public const int Full = 1500;

        /// <summary>5.12 s of audio.</summary>
        public const int Step = 256;

        private const int PositionsPerSecond = 50;

        // Room after the speech, so that its end is never at the very edge of what the encoder sees
        private const double MarginSeconds = 1.0;

        /// <summary>The audio context for this many 16 kHz samples: a multiple of <see cref="Step"/>, or <see cref="Full"/>.</summary>
        public static int For(int sampleCount)
        {
            double seconds = (double)Math.Max(sampleCount, 0) / AudioSamples.SampleRate + MarginSeconds;
            int positions = (int)Math.Ceiling(seconds * PositionsPerSecond);
            int stepped = (positions + Step - 1) / Step * Step;
            return stepped >= Full ? Full : stepped;
        }
    }
}
