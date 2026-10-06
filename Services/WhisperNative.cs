using System;
using System.Runtime.InteropServices;

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
        // whisper_sampling_strategy.WHISPER_SAMPLING_GREEDY, Whisper.net's default
        private const int SamplingGreedy = 0;

        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr> _initState;
        private static delegate* unmanaged[Cdecl]<IntPtr, void> _freeState;
        private static delegate* unmanaged[Cdecl]<int, WhisperFullParams*> _defaultParams;
        private static delegate* unmanaged[Cdecl]<WhisperFullParams*, void> _freeParams;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, WhisperFullParams, float*, int, int> _full;
        private static delegate* unmanaged[Cdecl]<IntPtr, int> _segmentCount;
        private static delegate* unmanaged[Cdecl]<IntPtr, int, byte*> _segmentText;

        /// <summary>Finds the functions in whisper.dll, before any of them is called.</summary>
        public static void Bind(IntPtr library)
        {
            _initState = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)Export(library, "whisper_init_state");
            _freeState = (delegate* unmanaged[Cdecl]<IntPtr, void>)Export(library, "whisper_free_state");
            _defaultParams = (delegate* unmanaged[Cdecl]<int, WhisperFullParams*>)Export(library, "whisper_full_default_params_by_ref");
            _freeParams = (delegate* unmanaged[Cdecl]<WhisperFullParams*, void>)Export(library, "whisper_free_params");
            _full = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, WhisperFullParams, float*, int, int>)Export(library, "whisper_full_with_state");
            _segmentCount = (delegate* unmanaged[Cdecl]<IntPtr, int>)Export(library, "whisper_full_n_segments_from_state");
            _segmentText = (delegate* unmanaged[Cdecl]<IntPtr, int, byte*>)Export(library, "whisper_full_get_segment_text_from_state");
        }

        /// <summary>A new state for the model's context: its compute buffers, on the model's device. Zero on failure.</summary>
        public static IntPtr InitState(IntPtr context) => _initState(context);

        public static void FreeState(IntPtr state) => _freeState(state);

        /// <summary>whisper.cpp's defaults for greedy decoding, which Whisper.net starts from too.</summary>
        public static WhisperFullParams DefaultParams()
        {
            WhisperFullParams* defaults = _defaultParams(SamplingGreedy);
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

        public static int SegmentCount(IntPtr state) => _segmentCount(state);

        public static string SegmentText(IntPtr state, int index) =>
            Marshal.PtrToStringUTF8((IntPtr)_segmentText(state, index)) ?? "";

        private static IntPtr Export(IntPtr library, string name) =>
            NativeLibrary.TryGetExport(library, name, out IntPtr address)
                ? address
                : throw new SpeechException(SpeechError.RuntimeMissing, $"whisper.dll has no {name}");
    }
}
