using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GB.Emulator.Core
{
    public class Memory
    {
        private readonly ushort[] memory;
        public Memory()
        {
            this.memory = new ushort[ushort.MaxValue];
        }
        public void Write(ushort value, ushort location)
        {
            try
            {
                this.memory[location] = value;
            }
            catch (IndexOutOfRangeException iore)
            {
                Trace.WriteLine($"Error writing {location:X2}:{value}");
                throw iore;
            }
        }

        public void Write(byte value, byte location)
        {
            try
            {
                ushort baseAddress = 0xFF00;

                int ushortLocation = baseAddress + location;
                this.memory[ushortLocation] = value;
            }
            catch (IndexOutOfRangeException iore)
            {
                Trace.WriteLine($"Error writing {location:X2}:{value}");
                throw iore;
            }
        }

        public ushort Read16(ushort location)
        {
            try
            {
                return this.memory[location];
            }
            catch (IndexOutOfRangeException iore)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw iore;
            }
        }

        public byte Read8(byte location)
        {
            try
            {
                ushort baseAddress = 0xFF00;
                int ushortLocation = baseAddress + location;

                ushort value = this.memory[ushortLocation];

                return (byte)(value >> 8);
            }
            catch (IndexOutOfRangeException iore)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw iore;
            }
        }

    }
}
