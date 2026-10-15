using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class WhisperThreadsTests
    {
        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX of a core in one processor group: an 8-byte header and a
        // PROCESSOR_RELATIONSHIP with one GROUP_AFFINITY
        private const int CoreRecordSize = 48;

        private static byte[] Records(params (int Relationship, int Size)[] records)
        {
            var bytes = new List<byte>();
            foreach ((int relationship, int size) in records)
            {
                var record = new byte[Math.Max(size, 8)];
                BinaryPrimitives.WriteInt32LittleEndian(record, relationship);
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)size);
                bytes.AddRange(record);
            }
            return bytes.ToArray();
        }

        [Theory]
        [InlineData(4, 8, 8, 4)]   // 4 cores with Hyper-Threading: one thread per core
        [InlineData(16, 32, 8, 8)] // more cores than the work needs
        [InlineData(8, 4, 8, 4)]   // the process may use only 4 of them
        [InlineData(0, 8, 8, 8)]   // cores unknown: one thread per logical processor, as before
        [InlineData(6, 12, 4, 4)]  // voice activity detection
        [InlineData(1, 1, 8, 1)]
        [InlineData(0, 0, 8, 1)]
        public void Threads_FollowThePhysicalCores(int cores, int logicalProcessors, int max, int expected) =>
            Assert.Equal(expected, WhisperThreads.For(cores, logicalProcessors, max));

        [Fact]
        public void CountCores_CountsEachCoreRecord() =>
            Assert.Equal(4, WhisperThreads.CountCores(Records((0, CoreRecordSize), (0, CoreRecordSize), (0, CoreRecordSize), (0, CoreRecordSize))));

        [Fact]
        public void CountCores_FollowsTheSizeOfEachRecord() =>
            // A core spanning more processor groups has a longer record; other relationships are skipped
            Assert.Equal(2, WhisperThreads.CountCores(Records((0, CoreRecordSize + 16), (2, 80), (0, CoreRecordSize))));

        [Fact]
        public void CountCores_StopsAtARecordThatDoesNotFit()
        {
            byte[] records = Records((0, CoreRecordSize), (0, CoreRecordSize));
            Assert.Equal(1, WhisperThreads.CountCores(records.AsSpan(0, records.Length - 1)));
            // A size of zero would never advance
            Assert.Equal(1, WhisperThreads.CountCores(Records((0, CoreRecordSize), (0, 0), (0, CoreRecordSize))));
            Assert.Equal(0, WhisperThreads.CountCores(Records((0, 4))));
        }

        [Fact]
        public void CountCores_OfNothingIsZero() =>
            Assert.Equal(0, WhisperThreads.CountCores(ReadOnlySpan<byte>.Empty));
    }
}
