using System;

namespace GB.Emulator.Core
{
    /// <summary>Standard MBC1 wiring: up to 2 MiB ROM, or 512 KiB ROM with 32 KiB RAM.</summary>
    internal sealed class Mbc1 : CartridgeMapper
    {
        private byte romBank;
        private byte secondaryBank;
        private bool advancedMode;

        public Mbc1(byte[] rom, int ramSize) : base(rom, ramSize) { }

        public override string BankingMode => this.advancedMode ? "Advanced banking" : "Simple banking";

        // Translate zero before masking disconnected address lines on smaller ROMs.
        public override int SelectedRomBank => this.MapRomBank(
            (this.secondaryBank << 5) | (this.romBank == 0 ? 1 : this.romBank));
        protected override int LowerRomBank => this.advancedMode
            ? this.MapRomBank(this.secondaryBank << 5) : 0;
        public override int SelectedRamBank => this.advancedMode && this.RomSize <= 0x80000
            ? this.MapRamBank(this.secondaryBank) : 0;

        public override void WriteControl(ushort address, byte value)
        {
            if (address < 0x2000) this.RamEnabled = (value & 0x0F) == 0x0A;
            else if (address < 0x4000) this.romBank = (byte)(value & 0x1F);
            else if (address < 0x6000) this.secondaryBank = (byte)(value & 0x03);
            else if (address < 0x8000) this.advancedMode = (value & 0x01) != 0;
        }

        public override CartridgeMapperState Snapshot() => new State(this.romBank,
            this.secondaryBank, this.advancedMode, this.RamEnabled, this.CaptureRam());

        public override void Restore(CartridgeMapperState state)
        {
            if (state is not State saved)
                throw new ArgumentException("Snapshot does not belong to an MBC1 cartridge.", nameof(state));
            this.RestoreRam(saved.Ram);
            this.romBank = saved.RomBank;
            this.secondaryBank = saved.SecondaryBank;
            this.advancedMode = saved.AdvancedMode;
            this.RamEnabled = saved.RamEnabled;
        }

        private sealed record State(byte RomBank, byte SecondaryBank, bool AdvancedMode,
            bool RamEnabled, byte[] Ram) : CartridgeMapperState;
    }
}
