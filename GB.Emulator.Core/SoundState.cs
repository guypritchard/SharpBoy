using System;

namespace GB.Emulator.Core
{
    /// <summary>An immutable view of the sound registers.</summary>
    public sealed class SoundState
    {
        private readonly byte[] registers;

        public SoundState(ReadOnlySpan<byte> registers)
        {
            if (registers.Length != 0x30)
                throw new ArgumentException("The sound register range has 48 bytes.", nameof(registers));
            this.registers = registers.ToArray();
        }

        public byte this[ushort address] => address is >= 0xFF10 and <= 0xFF3F
            ? this.registers[address - 0xFF10]
            : throw new ArgumentOutOfRangeException(nameof(address));
    }
}
