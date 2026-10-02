using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class MemoryMap
    {
        private readonly byte[] memory;
        private readonly List<IMemoryRange> devices;
        private readonly IMemoryRange[] deviceByAddress = new IMemoryRange[ushort.MaxValue + 1];
        private readonly Joypad joypad;
        private readonly SerialPort serial;
        private readonly Apu apu;
        private readonly Timer timer;
        private IMemoryAccessRecorder accessRecorder = new RecordingMemoryAccessRecorder();

        public MemoryMap(params IMemoryRange[] devices)
        {
            this.memory = new byte[ushort.MaxValue + 1];
            this.devices = new List<IMemoryRange>(devices);
            this.joypad = devices.OfType<Joypad>().FirstOrDefault();
            this.serial = devices.OfType<SerialPort>().FirstOrDefault();
            this.apu = devices.OfType<Apu>().FirstOrDefault();
            this.timer = devices.OfType<Timer>().FirstOrDefault();
            this.RebuildDeviceMap();
        }

        public void AddDevice(IMemoryRange device)
        {
            this.devices.Add(device);
            this.RebuildDeviceMap();
        }

        public void LoadRom(byte[] romData)
        {
            // Drop any previously loaded ROM device so the new one wins for 0x0000.
            this.devices.RemoveAll(d => d is Rom);

            int copyLength = Math.Min(romData.Length, this.memory.Length);
            Array.Copy(romData, 0, this.memory, 0, copyLength);

            // Insert at the front so reads hit ROM before any overlapping devices.
            this.devices.Insert(0, new Rom("ROM0", romData));
            this.RebuildDeviceMap();
        }

        public void Write8(byte value, ushort location)
        {
            try
            {
                this.memory[location] = value;

                var device = this.deviceByAddress[location];
                if (device != null)
                {
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

                this.accessRecorder.RecordWrite(location);

                if (location == 0xFF46)
                {
                    ushort source = (ushort)(value << 8);
                    for (int offset = 0; offset < 0xA0; offset++)
                    {
                        byte oamValue = this.Read8((ushort)(source + offset));
                        this.Write8(oamValue, (ushort)(0xFE00 + offset));
                    }
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
                ByteOp.Split(value, out byte high, out byte low);
                this.Write8(low, location);
                this.Write8(high, (ushort)(location + 1));
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
                byte low = this.Read8(location);
                byte high = this.Read8((ushort)(location + 1));
                return ByteOp.Concat(low, high);
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw;
            }
        }

        public byte Read8(ushort location)
        {
            try
            {
                var device = this.deviceByAddress[location];
                this.accessRecorder.RecordRead(location);
                if (device != null)
                {
                    try
                    {
                        byte value = device.Read8(location);
                        this.memory[location] = value;
                        return value;
                    }
                    catch (NotImplementedException)
                    {
                        // The device doesn't support reading `yet...
                        return this.memory[location];
                    }
                }

                return this.memory[location];
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw;
            }
        }

        public void Reset()
        {
            Array.Clear(this.memory, 0, this.memory.Length);
            this.accessRecorder.Reset();
        }

        internal void UseAccessRecorder(IMemoryAccessRecorder recorder)
        {
            if (ReferenceEquals(this.accessRecorder, recorder)) return;
            recorder.Reset();
            this.accessRecorder = recorder;
        }

        private void RebuildDeviceMap()
        {
            Array.Clear(this.deviceByAddress);
            // Earlier devices take priority when ranges overlap (ROM is inserted first).
            for (int i = this.devices.Count - 1; i >= 0; i--)
            {
                IMemoryRange device = this.devices[i];
                for (int address = device.Start; address <= device.End; address++)
                    this.deviceByAddress[address] = device;
            }
        }

        public byte Peek(ushort address)
        {
            if (address >= this.memory.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(address));
            }

            if (address == 0xFF00 && this.joypad != null) return this.joypad.Read8(address);
            if (address is 0xFF01 or 0xFF02 && this.serial != null) return this.serial.Read8(address);
            if (address is >= 0xFF10 and <= 0xFF3F && this.apu != null) return this.apu.Read8(address);
            if (address is >= 0xFF04 and <= 0xFF07 && this.timer != null) return this.timer.Read8(address);
            return this.memory[address];
        }

        public byte[] Snapshot()
        {
            var copy = new byte[this.memory.Length];
            Array.Copy(this.memory, copy, copy.Length);
            if (this.joypad != null) copy[0xFF00] = this.joypad.Read8(0xFF00);
            if (this.serial != null)
            {
                copy[0xFF01] = this.serial.Read8(0xFF01);
                copy[0xFF02] = this.serial.Read8(0xFF02);
            }
            if (this.apu != null)
                for (ushort address = 0xFF10; address <= 0xFF3F; address++)
                    copy[address] = this.apu.Read8(address);
            if (this.timer != null)
                for (ushort address = 0xFF04; address <= 0xFF07; address++)
                    copy[address] = this.timer.Read8(address);
            return copy;
        }

        public void RestoreSnapshot(byte[] snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.Length != this.memory.Length)
            {
                throw new ArgumentException("Snapshot size does not match memory size.", nameof(snapshot));
            }

            Array.Copy(snapshot, this.memory, this.memory.Length);
            this.accessRecorder.Reset();
        }

        internal IReadOnlyCollection<ushort> ConsumeRecentWrites() => this.accessRecorder.ConsumeWrites();

        internal IReadOnlyCollection<ushort> ConsumeRecentReads() => this.accessRecorder.ConsumeReads();
    }
}
