using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class WhisperFullParamsTests
    {
        // whisper_full_params of whisper.cpp 371b5a7 (Whisper.net 1.9.2-preview1): sizeof and offsetof as a C compiler
        // lays it out on a 64-bit target
        [Fact]
        public void Layout_MatchesWhisperCpp()
        {
            Assert.Equal(304, Unsafe.SizeOf<WhisperFullParams>());
            Assert.Equal(304, Marshal.SizeOf<WhisperFullParams>());
        }

        [Theory]
        [InlineData(nameof(WhisperFullParams.Threads), 4)]
        [InlineData(nameof(WhisperFullParams.Translate), 20)]
        [InlineData(nameof(WhisperFullParams.NoContext), 21)]
        [InlineData(nameof(WhisperFullParams.SingleSegment), 23)]
        [InlineData(nameof(WhisperFullParams.PrintProgress), 25)]
        [InlineData(nameof(WhisperFullParams.PrintTimestamps), 27)]
        [InlineData(nameof(WhisperFullParams.TokenTimestamps), 28)]
        [InlineData(nameof(WhisperFullParams.TokenTimestampThreshold), 32)]
        [InlineData(nameof(WhisperFullParams.MaxLength), 40)]
        [InlineData(nameof(WhisperFullParams.SplitOnWord), 44)]
        [InlineData(nameof(WhisperFullParams.MaxTokens), 48)]
        [InlineData(nameof(WhisperFullParams.DebugMode), 52)]
        [InlineData(nameof(WhisperFullParams.AudioContext), 56)]
        [InlineData(nameof(WhisperFullParams.TinyDiarize), 60)]
        [InlineData(nameof(WhisperFullParams.SuppressRegex), 64)]
        [InlineData(nameof(WhisperFullParams.InitialPrompt), 72)]
        [InlineData(nameof(WhisperFullParams.CarryInitialPrompt), 80)]
        [InlineData(nameof(WhisperFullParams.PromptTokens), 88)]
        [InlineData(nameof(WhisperFullParams.PromptTokenCount), 96)]
        [InlineData(nameof(WhisperFullParams.Language), 104)]
        [InlineData(nameof(WhisperFullParams.DetectLanguage), 112)]
        [InlineData(nameof(WhisperFullParams.SuppressBlank), 113)]
        [InlineData(nameof(WhisperFullParams.SuppressNonSpeechTokens), 114)]
        [InlineData(nameof(WhisperFullParams.Temperature), 116)]
        [InlineData(nameof(WhisperFullParams.NoSpeechThreshold), 140)]
        [InlineData(nameof(WhisperFullParams.GreedyBestOf), 144)]
        [InlineData(nameof(WhisperFullParams.BeamSize), 148)]
        [InlineData(nameof(WhisperFullParams.NewSegmentCallback), 160)]
        [InlineData(nameof(WhisperFullParams.AbortCallback), 208)]
        [InlineData(nameof(WhisperFullParams.AbortCallbackUserData), 216)]
        [InlineData(nameof(WhisperFullParams.LogitsFilterCallbackUserData), 232)]
        [InlineData(nameof(WhisperFullParams.GrammarRules), 240)]
        [InlineData(nameof(WhisperFullParams.GrammarRuleCount), 248)]
        [InlineData(nameof(WhisperFullParams.StartRule), 256)]
        [InlineData(nameof(WhisperFullParams.GrammarPenalty), 264)]
        [InlineData(nameof(WhisperFullParams.Vad), 268)]
        [InlineData(nameof(WhisperFullParams.VadModelPath), 272)]
        [InlineData(nameof(WhisperFullParams.VadThreshold), 280)]
        [InlineData(nameof(WhisperFullParams.VadSamplesOverlap), 300)]
        public void Field_IsWhereWhisperCppHasIt(string field, int offset) =>
            Assert.Equal(offset, (int)Marshal.OffsetOf<WhisperFullParams>(field));
    }
}
