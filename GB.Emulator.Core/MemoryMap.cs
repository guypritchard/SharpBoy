using System;
using System.Diagnostics;
using System.Linq;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class MemoryMap
    {
        private readonly byte[] memory;
        private readonly IMemoryRange[] devices;

        public MemoryMap(params IMemoryRange[] devices)
        {
            this.memory = new byte[ushort.MaxValue];
            this.devices = devices;
        }

        public void Write8(byte value, ushort location)
        {
            try
            {
                var device = this.devices.FirstOrDefault(d => location >= d.Start && location <= d.End);
                if (device == null)
                {
                    throw new NotImplementedException($"Aint nobody handling 0x{location:X2} for writing.");
                }
                
                try
                {
                    device.Write8(location, value);
                }
                catch (NotImplementedException)
                {
                    // The device doesn't support writing yet...
                    this.memory[location] = value;
                }
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error writing {location:X2}:{value}");
                throw;
            }
        }

        public void Write16(ushort value, ushort location)
        {
            try
            {
                ByteOp.Split(value, out byte low, out byte high);
                this.memory[location] = low;
                this.memory[location + 1] = high;

                var device = this.devices.FirstOrDefault(d => location >= d.Start && location <= d.End);
                if (device != null)
                {
                    device.Write8(location, low);
                    device.Write8((ushort)(location + 1), high);
                }
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error writing {location:X2}:{value}");
                throw;
            }
        }

        public ushort Read16(ushort location)
        {
            try
            {
                var b2 = ByteOp.Concat(this.memory[location + 1], this.memory[location]);
                return b2;
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw;
            }
        }

        public byte Read8(byte location)
        {
            try
            {
                var device = this.devices.FirstOrDefault(d => location >= d.Start && location <= d.End);
                if (device != null)
                {
                    try
                    {
                        return device.Read8(location);
                    }
                    catch (NotImplementedException)
                    {
                        // The device doesn't support reading `yet...
                        return this.memory[location];
                    }
                }

                throw new NotImplementedException("Aint nobody handling this memory location for reading.");
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw;
            }
        }
    }
}
