using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class SpriteTileManager : IMemoryRange
    {
        private const ushort StartAddress = 0x8000;
        private const ushort EndAddress = 0x8FFF;
        private readonly byte[] memory = new byte[EndAddress - StartAddress + 1];

        public ushort Start => StartAddress;

        public ushort End => EndAddress;

        public byte Read8(ushort location)
        {
            return this.memory[location - StartAddress];
        }

        public void Write8(ushort location, byte value)
        {
            this.memory[location - StartAddress] = value;
        }
    }
}
