using System;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    /// <summary>MBC3 cartridge ROM and RAM banking. Timer-less MBC3 carts do not expose RTC registers.</summary>
    internal sealed class Mbc3
    {
        private const int RomBankSize = 0x4000;
        private const int RamBankSize = 0x2000;
        private readonly byte[] rom;
        private readonly byte[] ram;
        private byte romBank = 1;
        private byte ramBank;
        private bool ramEnabled;

        public Mbc3(byte[] rom, int ramSize)
        {
            this.rom = rom;
            this.ram = new byte[ramSize];
            this.Rom = new RomRange(this);
            this.Ram = new RamRange(this);
        }

        public IMemoryRange Rom { get; }
        public IMemoryRange Ram { get; }
        public int SelectedRomBank => this.romBank;
        public int SelectedRamBank => this.ramBank;

        public byte ReadRom(ushort address)
        {
            int bank = address < RomBankSize ? 0 : this.romBank;
            int offset = bank * RomBankSize + (address & (RomBankSize - 1));
            return this.rom[offset % this.rom.Length];
        }

        public void WriteControl(ushort address, byte value)
        {
            if (address < 0x2000) this.ramEnabled = (value & 0x0F) == 0x0A;
            else if (address < 0x4000)
            {
                this.romBank = (byte)(value & 0x7F);
                if (this.romBank == 0) this.romBank = 1;
            }
            else if (address < 0x6000) this.ramBank = value;
        }

        public byte ReadRam(ushort address)
        {
            int offset = this.RamOffset(address);
            return offset < 0 ? (byte)0xFF : this.ram[offset];
        }

        public void WriteRam(ushort address, byte value)
        {
            int offset = this.RamOffset(address);
            if (offset >= 0) this.ram[offset] = value;
        }

        private int RamOffset(ushort address)
        {
            if (!this.ramEnabled || this.ram.Length == 0 || this.ramBank > 3) return -1;
            return (this.ramBank * RamBankSize + address - 0xA000) % this.ram.Length;
        }

        public State Snapshot() => new(this.romBank, this.ramBank, this.ramEnabled,
            (byte[])this.ram.Clone());

        public void Restore(State state)
        {
            if (state.Ram.Length != this.ram.Length)
                throw new ArgumentException("Cartridge RAM size does not match the loaded ROM.", nameof(state));
            this.romBank = state.RomBank;
            this.ramBank = state.RamBank;
            this.ramEnabled = state.RamEnabled;
            Array.Copy(state.Ram, this.ram, this.ram.Length);
        }

        internal readonly record struct State(byte RomBank, byte RamBank, bool RamEnabled, byte[] Ram);

        private sealed class RomRange : IMemoryRange
        {
            private readonly Mbc3 owner;
            public RomRange(Mbc3 owner) => this.owner = owner;
            public ushort Start => 0x0000;
            public ushort End => 0x7FFF;
            public byte Read8(ushort location) => this.owner.ReadRom(location);
            public void Write8(ushort location, byte value) => this.owner.WriteControl(location, value);
        }

        private sealed class RamRange : IMemoryRange
        {
            private readonly Mbc3 owner;
            public RamRange(Mbc3 owner) => this.owner = owner;
            public ushort Start => 0xA000;
            public ushort End => 0xBFFF;
            public byte Read8(ushort location) => this.owner.ReadRam(location);
            public void Write8(ushort location, byte value) => this.owner.WriteRam(location, value);
        }
    }
}
