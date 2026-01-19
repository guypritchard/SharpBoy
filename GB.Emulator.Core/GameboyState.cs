using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public sealed class GameboyState
    {
        internal GameboyState(
            CpuRegistersState registers,
            byte[] memory,
            byte[] ramBank1,
            byte[] ramBank2,
            byte[] ramBank3,
            byte[] internalRam,
            byte[] io,
            byte[] spriteTiles,
            byte[] backgroundTiles,
            Lcd.LcdState lcdState,
            byte interruptFlags)
        {
            this.Registers = registers;
            this.Memory = memory;
            this.RamBank1 = ramBank1;
            this.RamBank2 = ramBank2;
            this.RamBank3 = ramBank3;
            this.InternalRam = internalRam;
            this.Io = io;
            this.SpriteTiles = spriteTiles;
            this.BackgroundTiles = backgroundTiles;
            this.LcdState = lcdState;
            this.InterruptFlags = interruptFlags;
        }

        internal CpuRegistersState Registers { get; }

        internal byte[] Memory { get; }

        internal byte[] RamBank1 { get; }

        internal byte[] RamBank2 { get; }

        internal byte[] RamBank3 { get; }

        internal byte[] InternalRam { get; }

        internal byte[] Io { get; }

        internal byte[] SpriteTiles { get; }

        internal byte[] BackgroundTiles { get; }

        internal Lcd.LcdState LcdState { get; }

        internal byte InterruptFlags { get; }
    }

    internal readonly struct CpuRegistersState
    {
        public CpuRegistersState(
            byte a,
            byte f,
            byte b,
            byte c,
            byte d,
            byte e,
            byte h,
            byte l,
            byte flags,
            ushort sp,
            ushort pc)
        {
            this.A = a;
            this.F = f;
            this.B = b;
            this.C = c;
            this.D = d;
            this.E = e;
            this.H = h;
            this.L = l;
            this.Flags = flags;
            this.SP = sp;
            this.PC = pc;
        }

        public byte A { get; }

        public byte F { get; }

        public byte B { get; }

        public byte C { get; }

        public byte D { get; }

        public byte E { get; }

        public byte H { get; }

        public byte L { get; }

        public byte Flags { get; }

        public ushort SP { get; }

        public ushort PC { get; }
    }
}
