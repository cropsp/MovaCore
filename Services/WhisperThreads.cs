using System;
using System.Buffers.Binary;

namespace MovaCore.Services
{
    /// <summary>
    /// How many threads whisper.cpp computes with: one per physical core. All of ggml's threads do the same arithmetic,
    /// so the second thread of a core (SMT, Hyper-Threading) only competes with the first one for its units.
    /// </summary>
    public static class WhisperThreads
    {
        /// <summary>For recognition: more threads gain little, since the work waits on memory.</summary>
        public const int Max = 8;

        /// <summary>For voice activity detection: a tiny model.</summary>
        public const int MaxForVad = 4;

        // LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore
        private const int RelationProcessorCore = 0;

        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX starts with Relationship (Int32) and Size (UInt32)
        private const int HeaderSize = 8;

        /// <summary>
        /// The thread count for this many physical cores (0 if unknown) and logical processors: the latter counts only
        /// the processors this process may use.
        /// </summary>
        public static int For(int physicalCores, int logicalProcessors, int max)
        {
            int cores = physicalCores > 0 ? Math.Min(physicalCores, logicalProcessors) : logicalProcessors;
            return Math.Clamp(cores, 1, max);
        }

        /// <summary>
        /// Counts the processor cores in what GetLogicalProcessorInformationEx returns: records of varying size, each
        /// giving its own. Stops at a record that does not fit.
        /// </summary>
        internal static int CountCores(ReadOnlySpan<byte> records)
        {
            int cores = 0;
            while (records.Length >= HeaderSize)
            {
                int relationship = BinaryPrimitives.ReadInt32LittleEndian(records);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(records[4..]);
                if (size < HeaderSize || size > records.Length) break;
                if (relationship == RelationProcessorCore) cores++;
                records = records[(int)size..];
            }
            return cores;
        }
    }
}
