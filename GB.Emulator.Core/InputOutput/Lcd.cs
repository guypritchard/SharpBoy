using System;

namespace GB.Emulator.Core.InputOutput
{
    public class Lcd : IMemoryRange
    {
        public byte Control { get; set; }
        public byte ScrollY { get; set; }
        public byte ScrollX { get; set; }
        public byte Scanline { get; private set; }

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

        internal void Step()
        {
            this.Scanline = (byte)((this.Scanline + 1) % 154);
        }
    }
}
