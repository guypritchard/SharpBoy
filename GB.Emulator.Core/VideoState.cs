using System;

namespace GB.Emulator.Core
{
    /// <summary>An immutable view of the video hardware at one instant.</summary>
    public sealed class VideoState
    {
        private readonly byte[] vram = new byte[0x2000];
        private readonly byte[] oam = new byte[0xA0];
        private readonly byte[] registers = new byte[0x0C];

        public VideoState(ReadOnlySpan<byte> memory)
        {
            if (memory.Length < 0x10000)
                throw new ArgumentException("A full memory snapshot is required.", nameof(memory));

            memory.Slice(0x8000, this.vram.Length).CopyTo(this.vram);
            memory.Slice(0xFE00, this.oam.Length).CopyTo(this.oam);
            memory.Slice(0xFF40, this.registers.Length).CopyTo(this.registers);
        }

        public byte LcdControl => this.registers[0];
        public byte ScrollY => this.registers[2];
        public byte ScrollX => this.registers[3];
        public byte BackgroundPalette => this.registers[7];

        public byte this[int address] => address switch
        {
            >= 0x8000 and <= 0x9FFF => this.vram[address - 0x8000],
            >= 0xFE00 and <= 0xFE9F => this.oam[address - 0xFE00],
            >= 0xFF40 and <= 0xFF4B => this.registers[address - 0xFF40],
            _ => throw new ArgumentOutOfRangeException(nameof(address), "Address is outside video hardware.")
        };
    }
}
