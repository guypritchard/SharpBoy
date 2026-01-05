using System;

namespace GB.Emulator.Core.InputOutput
{
    public class Lcd : IMemoryRange
    {
        public byte Control { get; set; }
        public byte ScrollY { get; set; }
        public byte ScrollX { get; set; }
        public byte Scanline { get; private set; }

        public const int CyclesPerScanline = 456;

        public const int ScanlinesPerFrame = 154;

        private int accumulatedCycles = 0;

        public ushort Start => 0xff40;

        public ushort End => 0xff46;

        public byte Read8(ushort location)
        {
            switch (location)
            {
                case 0xff40:
                    return Control;
                case 0xff42:
                    return ScrollY;
                case 0xff43:
                    return ScrollX;
                case 0xff44:
                    return Scanline;
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
                    Control = value;
                    break;
                case 0xff42:
                    ScrollY = value;
                    break;
                case 0xff43:
                    ScrollX = value;
                    break;
                default:
                    // Not sure.
                    break;
            }
        }

        internal LcdState Snapshot()
        {
            return new LcdState(Control, ScrollY, ScrollX, Scanline, this.accumulatedCycles);
        }

        internal void Restore(LcdState snapshot)
        {
            Control = snapshot.Control;
            ScrollY = snapshot.ScrollY;
            ScrollX = snapshot.ScrollX;
            Scanline = snapshot.Scanline;
            this.accumulatedCycles = snapshot.AccumulatedCycles;
        }

        internal void Step(int cycles = 4)
        {
            this.accumulatedCycles += cycles;

            // There are 456 cycles per scanline.
            while (this.accumulatedCycles >= CyclesPerScanline)
            {
                // Move to the next scanline when we've accumulated enough cycles (456).
                this.accumulatedCycles -= CyclesPerScanline;
                this.Scanline = (byte)((this.Scanline + 1) % ScanlinesPerFrame);
            }
        }

        internal readonly struct LcdState
        {
            public LcdState(byte control, byte scrollY, byte scrollX, byte scanline, int accumulatedCycles)
            {
                this.Control = control;
                this.ScrollY = scrollY;
                this.ScrollX = scrollX;
                this.Scanline = scanline;
                this.AccumulatedCycles = accumulatedCycles;
            }

            public byte Control { get; }

            public byte ScrollY { get; }

            public byte ScrollX { get; }

            public byte Scanline { get; }

            public int AccumulatedCycles { get; }
        }
    }
}
