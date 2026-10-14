using System;

namespace GB.Emulator.Core
{
    /// <summary>MBC3 cartridge ROM and RAM banking. Timer-less MBC3 carts do not expose RTC registers.</summary>
    internal sealed class Mbc3 : CartridgeMapper
    {
        private byte romBank = 1;
        private byte ramBank;

        public Mbc3(byte[] rom, int ramSize) : base(rom, ramSize) { }

        public override int SelectedRomBank => this.romBank;
        public override int SelectedRamBank => this.ramBank;
        protected override bool CanAccessRam => base.CanAccessRam && this.ramBank <= 3;

        public override void WriteControl(ushort address, byte value)
        {
            if (address < 0x2000) this.RamEnabled = (value & 0x0F) == 0x0A;
            else if (address < 0x4000)
            {
                this.romBank = (byte)(value & 0x7F);
                if (this.romBank == 0) this.romBank = 1;
            }
            else if (address < 0x6000) this.ramBank = value;
        }

        public override CartridgeMapperState Snapshot() => new State(this.romBank,
            this.ramBank, this.RamEnabled, this.CaptureRam());

        public override void Restore(CartridgeMapperState state)
        {
            if (state is not State saved)
                throw new ArgumentException("Snapshot does not belong to an MBC3 cartridge.", nameof(state));
            this.RestoreRam(saved.Ram);
            this.romBank = saved.RomBank;
            this.ramBank = saved.RamBank;
            this.RamEnabled = saved.RamEnabled;
        }

        private sealed record State(byte RomBank, byte RamBank, bool RamEnabled,
            byte[] Ram) : CartridgeMapperState;
    }
}
