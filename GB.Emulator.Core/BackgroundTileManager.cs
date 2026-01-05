using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class BackgroundTileManager : IMemoryRange
    {
        private const ushort StartAddress = 0x9800;
        private const ushort EndAddress = 0x9BFF;
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

    internal byte[] Snapshot()
    {
        var copy = new byte[this.memory.Length];
        System.Array.Copy(this.memory, copy, copy.Length);
        return copy;
    }

    internal void Restore(byte[] snapshot)
    {
        if (snapshot == null)
        {
            throw new System.ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.Length != this.memory.Length)
        {
            throw new System.ArgumentException("Snapshot size does not match memory size.", nameof(snapshot));
        }

        System.Array.Copy(snapshot, this.memory, this.memory.Length);
    }
}
}
