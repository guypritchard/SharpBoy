using System;
using System.Collections.Generic;
using System.Linq;

namespace GB.Emulator.Core
{
    internal interface IMemoryAccessRecorder
    {
        void RecordRead(ushort address);
        void RecordWrite(ushort address);
        IReadOnlyCollection<ushort> ConsumeReads();
        IReadOnlyCollection<ushort> ConsumeWrites();
        void Reset();
    }

    internal sealed class RecordingMemoryAccessRecorder : IMemoryAccessRecorder
    {
        private readonly HashSet<ushort> reads = new();
        private readonly HashSet<ushort> writes = new();

        public void RecordRead(ushort address) => this.reads.Add(address);
        public void RecordWrite(ushort address) => this.writes.Add(address);

        public IReadOnlyCollection<ushort> ConsumeReads()
        {
            if (this.reads.Count == 0) return Array.Empty<ushort>();
            ushort[] result = this.reads.ToArray();
            this.reads.Clear();
            return result;
        }

        public IReadOnlyCollection<ushort> ConsumeWrites()
        {
            if (this.writes.Count == 0) return Array.Empty<ushort>();
            ushort[] result = this.writes.ToArray();
            this.writes.Clear();
            return result;
        }

        public void Reset()
        {
            this.reads.Clear();
            this.writes.Clear();
        }
    }

    internal sealed class SilentMemoryAccessRecorder : IMemoryAccessRecorder
    {
        public static readonly SilentMemoryAccessRecorder Instance = new();

        private SilentMemoryAccessRecorder() { }
        public void RecordRead(ushort address) { }
        public void RecordWrite(ushort address) { }
        public IReadOnlyCollection<ushort> ConsumeReads() => Array.Empty<ushort>();
        public IReadOnlyCollection<ushort> ConsumeWrites() => Array.Empty<ushort>();
        public void Reset() { }
    }
}
