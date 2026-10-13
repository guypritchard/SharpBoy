using System;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    /// <summary>Owns cartridge storage and exposes its two windows to the memory bus.</summary>
    internal abstract class CartridgeMapper
    {
        protected const int RomBankSize = 0x4000;
        protected const int RamBankSize = 0x2000;
        private readonly byte[] rom;
        private readonly byte[] ram;

        protected CartridgeMapper(byte[] rom, int ramSize)
        {
            ArgumentNullException.ThrowIfNull(rom);
            if (rom.Length == 0) throw new ArgumentException("Cartridge ROM cannot be empty.", nameof(rom));
            this.rom = rom;
            this.ram = new byte[ramSize];
            this.Rom = new RomRange(this);
            this.Ram = new RamRange(this);
        }

        public IMemoryRange Rom { get; }
        public IMemoryRange Ram { get; }
        public abstract int SelectedRomBank { get; }
        public abstract int SelectedRamBank { get; }
        protected virtual int LowerRomBank => 0;
        public int SelectedLowerRomBank => this.LowerRomBank;
        public bool IsRamEnabled => this.CanAccessRam;
        public virtual string BankingMode => "Independent ROM / RAM";
        protected bool RamEnabled { get; set; }
        protected int RomSize => this.rom.Length;
        protected virtual bool CanAccessRam => this.RamEnabled && this.ram.Length > 0;

        protected int MapRomBank(int bank) => bank % Math.Max(1, this.rom.Length / RomBankSize);
        protected int MapRamBank(int bank) => bank % Math.Max(1, this.ram.Length / RamBankSize);

        public byte ReadRom(ushort address)
        {
            int bank = address < RomBankSize ? this.LowerRomBank : this.SelectedRomBank;
            int offset = bank * RomBankSize + (address & (RomBankSize - 1));
            return this.rom[offset % this.rom.Length];
        }

        public abstract void WriteControl(ushort address, byte value);

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

        private int RamOffset(ushort address) => !this.CanAccessRam ? -1 :
            (this.SelectedRamBank * RamBankSize + address - 0xA000) % this.ram.Length;

        protected byte[] CaptureRam() => (byte[])this.ram.Clone();

        protected void RestoreRam(byte[] savedRam)
        {
            if (savedRam.Length != this.ram.Length)
                throw new ArgumentException("Cartridge RAM size does not match the loaded ROM.", nameof(savedRam));
            Array.Copy(savedRam, this.ram, this.ram.Length);
        }

        public abstract CartridgeMapperState Snapshot();
        public abstract void Restore(CartridgeMapperState state);

        private sealed class RomRange : IMemoryRange
        {
            private readonly CartridgeMapper owner;
            public RomRange(CartridgeMapper owner) => this.owner = owner;
            public ushort Start => 0x0000;
            public ushort End => 0x7FFF;
            public byte Read8(ushort location) => this.owner.ReadRom(location);
            public void Write8(ushort location, byte value) => this.owner.WriteControl(location, value);
        }

        private sealed class RamRange : IMemoryRange
        {
            private readonly CartridgeMapper owner;
            public RamRange(CartridgeMapper owner) => this.owner = owner;
            public ushort Start => 0xA000;
            public ushort End => 0xBFFF;
            public byte Read8(ushort location) => this.owner.ReadRam(location);
            public void Write8(ushort location, byte value) => this.owner.WriteRam(location, value);
        }
    }

    internal abstract record CartridgeMapperState;
}
