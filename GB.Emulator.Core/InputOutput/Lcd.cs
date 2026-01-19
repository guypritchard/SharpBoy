using System;

namespace GB.Emulator.Core.InputOutput
{
    public class Lcd : IMemoryRange
    {
        public byte Control { get; set; }
        public byte ScrollY { get; set; }
        public byte ScrollX { get; set; }
        public byte Scanline { get; private set; }
        public byte LyCompare { get; private set; }

        public const int CyclesPerScanline = 456;

        public const int ScanlinesPerFrame = 154;

        public const int VBlankStartScanline = 144;

        private const byte StatHBlankInterrupt = 0x08;
        private const byte StatVBlankInterrupt = 0x10;
        private const byte StatOamInterrupt = 0x20;
        private const byte StatLycInterrupt = 0x40;
        private const byte StatAlwaysOn = 0x80;
        private const byte InterruptVBlank = 0x01;
        private const byte InterruptStat = 0x02;

        private int accumulatedCycles = 0;
        private byte statControl;
        private byte mode;
        private bool lastCoincidence;

        public ushort Start => 0xff40;

        public ushort End => 0xff46;

        public byte Read8(ushort location)
        {
            switch (location)
            {
                case 0xff40:
                    return Control;
                case 0xff41:
                    return BuildStat();
                case 0xff42:
                    return ScrollY;
                case 0xff43:
                    return ScrollX;
                case 0xff44:
                    return Scanline;
                case 0xff45:
                    return LyCompare;
                default:
                    // Not sure.
                    return 0;
            }
        }

        public void Write8(ushort location, byte value)
        {
            switch (location)
            {
                case 0xff40:
                    bool wasEnabled = IsLcdEnabled;
                    Control = value;
                    if (wasEnabled != IsLcdEnabled)
                    {
                        ResetTiming();
                    }
                    break;
                case 0xff41:
                    // Bits 3-6 are writable; everything else is read-only.
                    statControl = (byte)(value & 0x78);
                    break;
                case 0xff42:
                    ScrollY = value;
                    break;
                case 0xff43:
                    ScrollX = value;
                    break;
                case 0xff45:
                    LyCompare = value;
                    lastCoincidence = Scanline == LyCompare;
                    break;
                default:
                    // Not sure.
                    break;
            }
        }

        internal LcdState Snapshot()
        {
            return new LcdState(Control, ScrollY, ScrollX, Scanline, LyCompare, statControl, this.accumulatedCycles);
        }

        internal void Restore(LcdState snapshot)
        {
            Control = snapshot.Control;
            ScrollY = snapshot.ScrollY;
            ScrollX = snapshot.ScrollX;
            Scanline = snapshot.Scanline;
            LyCompare = snapshot.LyCompare;
            statControl = snapshot.StatControl;
            this.accumulatedCycles = snapshot.AccumulatedCycles;
            UpdateDerivedState();
        }

        // Returns interrupt requests to OR into IF (bit 0 = VBlank, bit 1 = STAT).
        internal byte Step(int cycles = 4)
        {
            if (!IsLcdEnabled)
            {
                ResetTiming();
                return 0;
            }

            byte previousMode = this.mode;
            bool previousCoincidence = this.lastCoincidence;
            byte interruptRequest = 0;

            this.accumulatedCycles += cycles;
            bool enteredVBlank = false;

            // There are 456 cycles per scanline.
            while (this.accumulatedCycles >= CyclesPerScanline)
            {
                // Move to the next scanline when we've accumulated enough cycles (456).
                this.accumulatedCycles -= CyclesPerScanline;
                byte nextScanline = (byte)((this.Scanline + 1) % ScanlinesPerFrame);
                if (this.Scanline < VBlankStartScanline && nextScanline == VBlankStartScanline)
                {
                    enteredVBlank = true;
                }

                this.Scanline = nextScanline;
            }

            if (enteredVBlank)
            {
                interruptRequest |= InterruptVBlank;
            }

            this.mode = CalculateMode(this.Scanline, this.accumulatedCycles);
            bool coincidence = this.Scanline == this.LyCompare;
            this.lastCoincidence = coincidence;

            if (this.mode != previousMode)
            {
                interruptRequest |= GetModeInterruptRequest(this.mode);
            }

            if (!previousCoincidence && coincidence && (statControl & StatLycInterrupt) != 0)
            {
                interruptRequest |= InterruptStat;
            }

            return interruptRequest;
        }

        private bool IsLcdEnabled => (Control & 0x80) != 0;

        private void ResetTiming()
        {
            this.accumulatedCycles = 0;
            this.Scanline = 0;
            this.mode = 0;
            this.lastCoincidence = this.Scanline == this.LyCompare;
        }

        private void UpdateDerivedState()
        {
            this.mode = CalculateMode(this.Scanline, this.accumulatedCycles);
            this.lastCoincidence = this.Scanline == this.LyCompare;
        }

        private byte BuildStat()
        {
            // STAT is composed of interrupt enables (3-6), coincidence flag (2), mode (1-0), and bit 7 set.
            byte stat = (byte)(statControl & 0x78);
            if (this.Scanline == this.LyCompare)
            {
                stat |= 0x04;
            }

            stat |= (byte)(this.mode & 0x03);
            stat |= StatAlwaysOn;
            return stat;
        }

        private static byte CalculateMode(byte scanline, int dotPosition)
        {
            if (scanline >= VBlankStartScanline)
            {
                return 1; // VBlank
            }

            if (dotPosition < 80)
            {
                return 2; // OAM
            }

            if (dotPosition < 80 + 172)
            {
                return 3; // Transfer
            }

            return 0; // HBlank
        }

        private byte GetModeInterruptRequest(byte newMode)
        {
            return newMode switch
            {
                0 when (statControl & StatHBlankInterrupt) != 0 => InterruptStat,
                1 when (statControl & StatVBlankInterrupt) != 0 => InterruptStat,
                2 when (statControl & StatOamInterrupt) != 0 => InterruptStat,
                _ => 0
            };
        }

        internal readonly struct LcdState
        {
            public LcdState(byte control, byte scrollY, byte scrollX, byte scanline, byte lyCompare, byte statControl, int accumulatedCycles)
            {
                this.Control = control;
                this.ScrollY = scrollY;
                this.ScrollX = scrollX;
                this.Scanline = scanline;
                this.LyCompare = lyCompare;
                this.StatControl = statControl;
                this.AccumulatedCycles = accumulatedCycles;
            }

            public byte Control { get; }

            public byte ScrollY { get; }

            public byte ScrollX { get; }

            public byte Scanline { get; }

            public byte LyCompare { get; }

            public byte StatControl { get; }

            public int AccumulatedCycles { get; }
        }
    }
}
