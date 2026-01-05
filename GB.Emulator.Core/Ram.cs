using GB.Emulator.Core.InputOutput;
using System.Diagnostics;

namespace GB.Emulator.Core
{
    public class Ram : IMemoryRange
    {
        private readonly byte[] memory;
        public Ram(string name, ushort start, ushort end)
        {
            this.Name = name;
            this.Start = start;
            this.End = end;
            this.memory = new byte[end - start + 1];
        }

        public string Name { get; }
        public ushort Start
        {
            get; private set;
        }

        public ushort End
        {
            get; private set;
        }

        public void Write8(ushort location, byte value)
        {
            Trace.WriteLine($"{this.Name}: {value}->0x{location:X2}");
            this.memory[location - this.Start] = value;
        }

    public byte Read8(ushort location)
    {
        var value = this.memory[location - this.Start];
        Trace.WriteLine($"{this.Name}: {value}<-0x{location:X2}");
        return value;
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
