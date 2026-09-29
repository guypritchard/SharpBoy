using System;

namespace GB.Emulator.Core.InputOutput
{
    /// <summary>Sound register hardware. Audio synthesis can be added behind this device.</summary>
    public sealed class Apu : IMemoryRange
    {
        private readonly byte[] registers = new byte[0x30];

        public ushort Start => 0xFF10;
        public ushort End => 0xFF3F;

        public byte Read8(ushort location) => this.registers[location - Start];

        public void Write8(ushort location, byte value) => this.registers[location - Start] = value;

        public SoundState CaptureState() => new(this.registers);

        internal byte[] Snapshot() => (byte[])this.registers.Clone();

        internal void Restore(byte[] snapshot)
        {
            if (snapshot.Length != this.registers.Length)
                throw new ArgumentException("Sound register snapshot has the wrong size.", nameof(snapshot));
            snapshot.CopyTo(this.registers, 0);
        }

        internal void Reset() => Array.Clear(this.registers);
    }
}
