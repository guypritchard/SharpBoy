using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class MemoryMap
    {
        private const ushort OamDmaAddress = 0xFF46;
        private const ushort OamStartAddress = 0xFE00;
        private const int OamLength = 0xA0;
        private readonly byte[] memory;
        private readonly List<IMemoryRange> devices;
        private readonly IMemoryRange[] deviceByAddress = new IMemoryRange[ushort.MaxValue + 1];
        private readonly Joypad joypad;
        private readonly SerialPort serial;
        private readonly Apu apu;
        private readonly Timer timer;
        private readonly Lcd lcd;
        private CartridgeMapper cartridgeMapper;
        private IMemoryAccessRecorder accessRecorder = new RecordingMemoryAccessRecorder();

        public MemoryMap(params IMemoryRange[] devices)
        {
            this.memory = new byte[ushort.MaxValue + 1];
            this.devices = new List<IMemoryRange>(devices);
            this.joypad = devices.OfType<Joypad>().FirstOrDefault();
            this.serial = devices.OfType<SerialPort>().FirstOrDefault();
            this.apu = devices.OfType<Apu>().FirstOrDefault();
            this.timer = devices.OfType<Timer>().FirstOrDefault();
            this.lcd = devices.OfType<Lcd>().FirstOrDefault();
            this.RebuildDeviceMap();
        }

        public void AddDevice(IMemoryRange device) => this.AddDevice(device, takePriority: false);

        public void AddDevice(IMemoryRange device, bool takePriority)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (device.Start > device.End) throw new ArgumentException("Device range is inverted.", nameof(device));
            if (takePriority) this.devices.Insert(0, device);
            else this.devices.Add(device);
            this.RebuildDeviceMap();
        }

        public void LoadRom(byte[] romData)
        {
            this.RemoveCartridgeDevices();

            // Only $0000-$7FFF is cartridge ROM. Extra banks belong to the mapper;
            // copying them beyond that window corrupts RAM and I/O snapshot values.
            int copyLength = Math.Min(romData.Length, 0x8000);
            Array.Copy(romData, 0, this.memory, 0, copyLength);
            this.AddCartridgeDevices(romData);
            this.RebuildDeviceMap();
        }

        private void RemoveCartridgeDevices()
        {
            IMemoryRange oldMbcRom = this.cartridgeMapper?.Rom;
            IMemoryRange oldMbcRam = this.cartridgeMapper?.Ram;
            this.devices.RemoveAll(d => d is Rom || ReferenceEquals(d, oldMbcRom) ||
                ReferenceEquals(d, oldMbcRam));
            this.cartridgeMapper = null;
        }

        private void AddCartridgeDevices(byte[] romData)
        {
            // Insert cartridge windows first so they win over the generic address-space backing.
            this.cartridgeMapper = CartridgeMapperFactory.Create(romData);
            if (this.cartridgeMapper != null)
            {
                this.devices.Insert(0, this.cartridgeMapper.Ram);
                this.devices.Insert(0, this.cartridgeMapper.Rom);
            }
            else
            {
                this.devices.Insert(0, new Rom("ROM0", romData));
            }
        }

        internal CartridgeMapperState CaptureCartridgeState() => this.cartridgeMapper?.Snapshot();

        public int SelectedRomBank => this.cartridgeMapper?.SelectedRomBank ?? 1;

        public int SelectedLowerRomBank => this.cartridgeMapper?.SelectedLowerRomBank ?? 0;

        public bool CartridgeRamEnabled => this.cartridgeMapper?.IsRamEnabled ?? false;

        public string CartridgeBankingMode => this.cartridgeMapper?.BankingMode ?? "Fixed ROM";

        public int? SelectedCartridgeRamBank => this.cartridgeMapper?.SelectedRamBank;

        internal void RestoreCartridgeState(CartridgeMapperState state)
        {
            if (state != null)
            {
                this.cartridgeMapper?.Restore(state);
            }
        }

        public void Write8(byte value, ushort location)
        {
            try
            {
                this.memory[location] = value;
                this.WriteMappedDevice(location, value);
                this.accessRecorder.RecordWrite(location);

                if (location == OamDmaAddress)
                {
                    this.TransferOam(value);
                }
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error writing {location:X2}:{value}");
                throw;
            }
        }

        private void WriteMappedDevice(ushort address, byte value)
        {
            IMemoryRange device = this.deviceByAddress[address];
            if (device == null)
            {
                return;
            }

            try
            {
                device.Write8(address, value);
            }
            catch (NotImplementedException)
            {
                // Incomplete devices retain writes in the backing memory.
                this.memory[address] = value;
            }
        }

        private void TransferOam(byte sourcePage)
        {
            ushort sourceAddress = (ushort)(sourcePage << 8);
            for (int offset = 0; offset < OamLength; offset++)
            {
                byte value = this.Read8((ushort)(sourceAddress + offset));
                this.Write8(value, (ushort)(OamStartAddress + offset));
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
                this.accessRecorder.RecordRead(location);
                return this.ReadMappedDevice(location);
            }
            catch (IndexOutOfRangeException)
            {
                Trace.WriteLine($"Error reading {location:X2}");
                throw;
            }
        }

        private byte ReadMappedDevice(ushort address)
        {
            IMemoryRange device = this.deviceByAddress[address];
            if (device == null)
            {
                return this.memory[address];
            }

            try
            {
                byte value = device.Read8(address);
                this.memory[address] = value;
                return value;
            }
            catch (NotImplementedException)
            {
                // Incomplete devices read from the backing memory.
                return this.memory[address];
            }
        }

        public void Reset()
        {
            Array.Clear(this.memory, 0, this.memory.Length);
            this.accessRecorder.Reset();
        }

        internal void UseAccessRecorder(IMemoryAccessRecorder recorder)
        {
            if (ReferenceEquals(this.accessRecorder, recorder))
            {
                return;
            }
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
                {
                    this.deviceByAddress[address] = device;
                }
            }
        }

        public byte Peek(ushort address)
        {
            if (this.deviceByAddress[address] is IInspectableMemoryRange peripheral)
                return peripheral.Peek(address);
            if (address >= this.memory.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(address));
            }

            if (address is >= 0xFF40 and <= 0xFF45 && this.lcd != null)
                return this.lcd.Read8(address);

            // Live registers may change between bus accesses. Inspect them without
            // updating the backing memory or recording debugger activity.
            if (address == 0xFF00 && this.joypad != null)
            {
                return this.joypad.Read8(address);
            }

            if (address is 0xFF01 or 0xFF02 && this.serial != null)
            {
                return this.serial.Read8(address);
            }

            if (address is >= 0xFF10 and <= 0xFF3F && this.apu != null)
            {
                return this.apu.Read8(address);
            }

            if (address is >= 0xFF04 and <= 0xFF07 && this.timer != null)
            {
                return this.timer.Read8(address);
            }

            if (this.cartridgeMapper != null && (address <= 0x7FFF || address is >= 0xA000 and <= 0xBFFF))
            {
                return this.deviceByAddress[address].Read8(address);
            }

            return this.memory[address];
        }

        public byte[] Snapshot()
        {
            var copy = new byte[this.memory.Length];
            Array.Copy(this.memory, copy, copy.Length);
            this.CopyDeviceRegisters(this.joypad, copy);
            this.CopyDeviceRegisters(this.serial, copy);
            this.CopyDeviceRegisters(this.apu, copy);
            this.CopyDeviceRegisters(this.timer, copy);
            if (this.lcd != null)
                for (ushort address = 0xFF40; address <= 0xFF45; address++)
                    copy[address] = this.lcd.Read8(address);
            this.CopyDeviceRegisters(this.cartridgeMapper?.Rom, copy);
            this.CopyDeviceRegisters(this.cartridgeMapper?.Ram, copy);
            foreach (IInspectableMemoryRange peripheral in this.devices.OfType<IInspectableMemoryRange>())
                for (int address = peripheral.Start; address <= peripheral.End; address++)
                    if (ReferenceEquals(this.deviceByAddress[address], peripheral))
                        copy[address] = peripheral.Peek((ushort)address);
            return copy;
        }

        private void CopyDeviceRegisters(IMemoryRange device, byte[] snapshot)
        {
            if (device == null)
            {
                return;
            }

            for (int address = device.Start; address <= device.End; address++)
            {
                snapshot[address] = device.Read8((ushort)address);
            }
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
