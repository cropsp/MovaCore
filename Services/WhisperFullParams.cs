using System;
using System.Runtime.InteropServices;

namespace MovaCore.Services
{
    /// <summary>
    /// whisper.cpp's whisper_full_params, field for field, as in the whisper.cpp that Whisper.net 1.9.2-preview1 ships
    /// (commit 371b5a7). whisper_full_with_state takes it by value, so the layout must match exactly: the nested greedy,
    /// beam_search and vad_params structs are spelled out field by field, which keeps it. When Whisper.net is updated,
    /// compare this with its whisper.h; WhisperFullParamsTests holds the size and offsets of this version.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WhisperFullParams
    {
        public int Strategy;
        public int Threads;
        public int MaxTextContext;
        public int OffsetMs;
        public int DurationMs;

        public byte Translate;
        public byte NoContext;
        public byte NoTimestamps;
        public byte SingleSegment;
        public byte PrintSpecial;
        public byte PrintProgress;
        public byte PrintRealtime;
        public byte PrintTimestamps;

        public byte TokenTimestamps;
        public float TokenTimestampThreshold;
        public float TokenTimestampSumThreshold;
        public int MaxLength;
        public byte SplitOnWord;
        public int MaxTokens;

        public byte DebugMode;
        public int AudioContext;

        public byte TinyDiarize;

        public IntPtr SuppressRegex;

        public IntPtr InitialPrompt;
        public byte CarryInitialPrompt;
        public IntPtr PromptTokens;
        public int PromptTokenCount;

        public IntPtr Language;
        public byte DetectLanguage;

        public byte SuppressBlank;
        public byte SuppressNonSpeechTokens;

        public float Temperature;
        public float MaxInitialTimestamp;
        public float LengthPenalty;

        public float TemperatureIncrement;
        public float EntropyThreshold;
        public float LogProbThreshold;
        public float NoSpeechThreshold;

        public int GreedyBestOf;

        public int BeamSize;
        public float BeamPatience;

        public IntPtr NewSegmentCallback;
        public IntPtr NewSegmentCallbackUserData;
        public IntPtr ProgressCallback;
        public IntPtr ProgressCallbackUserData;
        public IntPtr EncoderBeginCallback;
        public IntPtr EncoderBeginCallbackUserData;
        public IntPtr AbortCallback;
        public IntPtr AbortCallbackUserData;
        public IntPtr LogitsFilterCallback;
        public IntPtr LogitsFilterCallbackUserData;

        public IntPtr GrammarRules;
        public nuint GrammarRuleCount;
        public nuint StartRule;
        public float GrammarPenalty;

        public byte Vad;
        public IntPtr VadModelPath;
        public float VadThreshold;
        public int VadMinSpeechDurationMs;
        public int VadMinSilenceDurationMs;
        public float VadMaxSpeechDurationS;
        public int VadSpeechPadMs;
        public float VadSamplesOverlap;
    }
}
