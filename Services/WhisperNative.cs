using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MovaCore.Services
{
    /// <summary>
    /// The whisper.cpp functions MovaCore calls itself, from the whisper.dll that the recognizer loaded: the very module
    /// Whisper.net uses, so they work on its model. Whisper.net creates and frees a whisper state (the compute buffers,
    /// hundreds of megabytes on a graphics card) on every transcription, which took half the time of a short phrase on
    /// an RTX 4060; through these calls one state serves every phrase.
    /// </summary>
    internal static unsafe class WhisperNative
    {
        // whisper_sampling_strategy
        public const int SamplingGreedy = 0;
        public const int SamplingBeamSearch = 1;

        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _initState;
        private static delegate* unmanaged[Cdecl]<IntPtr, void> _freeState;
        private static delegate* unmanaged[Cdecl]<int, WhisperFullParams*> _defaultParams;
        private static delegate* unmanaged[Cdecl]<WhisperFullParams*, void> _freeParams;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, WhisperFullParams, float*, int, int> _full;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> _segmentCount;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int> _tokenCount;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, int, int> _tokenId;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, byte*> _tokenText;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> _endOfText;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> _firstTimestamp;

        /// <summary>Finds the functions in whisper.dll, before any of them is called.</summary>
        public static void Bind(IntPtr library)
        {
            _initState = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)Export(library, "whisper_init_state");
            _freeState = (delegate* unmanaged[Cdecl]<IntPtr, void>)Export(library, "whisper_free_state");
            _defaultParams = (delegate* unmanaged[Cdecl]<int, WhisperFullParams*>)Export(library, "whisper_full_default_params_by_ref");
            _freeParams = (delegate* unmanaged[Cdecl]<WhisperFullParams*, void>)Export(library, "whisper_free_params");
            _full = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, WhisperFullParams, float*, int, int>)Export(library, "whisper_full_with_state");
            _segmentCount = (delegate* unmanaged[Cdecl]<IntPtr, int>)Export(library, "whisper_full_n_segments_from_state");
            _tokenCount = (delegate* unmanaged[Cdecl]<IntPtr, int, int>)Export(library, "whisper_full_n_tokens_from_state");
            _tokenId = (delegate* unmanaged[Cdecl]<IntPtr, int, int, int>)Export(library, "whisper_full_get_token_id_from_state");
            _tokenText = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int, byte*>)Export(library, "whisper_full_get_token_text_from_state");
            _endOfText = (delegate* unmanaged[Cdecl]<IntPtr, int>)Export(library, "whisper_token_eot");
            _firstTimestamp = (delegate* unmanaged[Cdecl]<IntPtr, int>)Export(library, "whisper_token_beg");
        }

        /// <summary>A new state for the model's context: its compute buffers, on the model's device. Zero on failure.</summary>
        public static IntPtr InitState(IntPtr context) => _initState(context);

        public static void FreeState(IntPtr state) => _freeState(state);

        /// <summary>whisper.cpp's defaults for a decoding strategy (beam search: 5 beams), as Whisper.net starts from.</summary>
        public static WhisperFullParams DefaultParams(int strategy)
        {
            WhisperFullParams* defaults = _defaultParams(strategy);
            try
            {
                return *defaults;
            }
            finally
            {
                _freeParams(defaults);
            }
        }

        /// <summary>Transcribes 16 kHz mono samples into the state; 0 on success.</summary>
        public static int Full(IntPtr context, IntPtr state, WhisperFullParams parameters, float* samples, int count) =>
            _full(context, state, parameters, samples, count);

        /// <summary>
        /// The recognized text in the parts Whisper itself marked with timestamps. A segment can hold several, and always
        /// does with a single segment; when Whisper repeats itself, the repeat is a part of its own.
        /// </summary>
        public static List<string> ReadParts(IntPtr context, IntPtr state)
        {
            int endOfText = _endOfText(context), firstTimestamp = _firstTimestamp(context);
            var parts = new List<string>();
            // Tokens are bytes, and a Cyrillic letter can be split between two of them: a part is decoded whole
            var text = new List<byte>();
            int segments = _segmentCount(state);
            for (int segment = 0; segment < segments; segment++)
            {
                int tokens = _tokenCount(state, segment);
                for (int i = 0; i < tokens; i++)
                {
                    int id = _tokenId(state, segment, i);
                    if (id < endOfText)
                        text.AddRange(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_tokenText(context, state, segment, i)));
                    else if (id >= firstTimestamp)
                        EndPart(text, parts);
                }
                EndPart(text, parts);
            }
            return parts;
        }

        private static void EndPart(List<byte> text, List<string> parts)
        {
            if (text.Count == 0) return;
            parts.Add(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(text)));
            text.Clear();
        }

        private static IntPtr Export(IntPtr library, string name) =>
            NativeLibrary.TryGetExport(library, name, out IntPtr address)
                ? address
                : throw new SpeechException(SpeechError.RuntimeMissing, $"whisper.dll has no {name}");
    }
}
