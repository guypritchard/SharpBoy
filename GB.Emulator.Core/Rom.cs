using GB.Emulator.Core.InputOutput;
using System;
using System.Diagnostics;

namespace GB.Emulator.Core
{
    public class Rom : IMemoryRange
    {
        private readonly byte[] memory;
        public Rom(string name, byte[] data)
        {
            this.Name = name;
            this.Start = 0x0000;
            this.End = 0x3FFF;
            this.memory = new byte[0x4000];
            Array.Copy(data, 0, this.memory, 0, Math.Min(data.Length, this.memory.Length));
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
           // ROM is read-only for now.
        }

        public byte Read8(ushort location)
        {
            var value = this.memory[location - this.Start];
            Trace.WriteLine($"{this.Name}: {value}<-0x{location:X2}");
            return value;
        }
    }
}
