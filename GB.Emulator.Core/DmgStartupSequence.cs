using System;
using System.Diagnostics;
using System.Text;

namespace GB.Emulator.Core
{
    /// <summary>
    /// Clean-room, frame-granular DMG startup driven by hardware registers, not boot ROM opcodes.
    /// One AdvanceFrame call clocks exactly 154 scanlines; the cartridge CPU stays at $0100.
    /// </summary>
    internal sealed class DmgStartupSequence
    {
        private const int LogoStart = 0x104;
        private const int LogoLength = 48;
        private const int ScrollFrames = 200;
        private const int TotalFrames = 264;

        // Header signature, not boot ROM machine code. The artwork is read from the cartridge to draw it.
        private static readonly byte[] ExpectedLogo =
        {
            0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
            0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
            0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
            0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
            0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC,
            0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E
        };

        private bool initialized;
        public bool IsActive { get; private set; }
        public int FramesCompleted { get; private set; }

        public void Restart()
        {
            this.initialized = false;
            this.FramesCompleted = 0;
            this.IsActive = true;
        }

        public StartupState CaptureState() => new(this.initialized, this.IsActive, this.FramesCompleted);

        public void RestoreState(StartupState state)
        {
            if (state.FramesCompleted < 0 || state.FramesCompleted > TotalFrames)
                throw new ArgumentOutOfRangeException(nameof(state));
            this.initialized = state.Initialized;
            this.IsActive = state.IsActive;
            this.FramesCompleted = state.FramesCompleted;
        }

        public void AdvanceFrame(MemoryMap memory, Cpu cpu, byte[] cartridge)
        {
            if (!this.IsActive) throw new InvalidOperationException("DMG startup has already completed.");
            if (!this.initialized)
            {
                this.Initialize(memory, cartridge);
                this.initialized = true;
            }

            int frame = this.FramesCompleted + 1;
            // Apply each even frame's scroll/tone before clocking its peripherals so
            // FrameReady observes the same state that produced the emitted frame.
            if (frame <= ScrollFrames && (frame & 1) == 0)
            {
                int count = frame / 2;
                if (count == 0x62) PlayTone(memory, 0x83);
                if (count == 0x64) PlayTone(memory, 0xC1);
                memory.Write8((byte)(100 - count), 0xFF42);
            }

            cpu.AdvanceStartupFrame();
            this.FramesCompleted = frame;

            if (this.FramesCompleted == TotalFrames)
            {
                this.Handoff(memory, cpu);
                this.IsActive = false;
            }
        }

        private void Initialize(MemoryMap memory, byte[] cartridge)
        {
            for (int address = 0x8000; address <= 0x9FFF; address++)
                memory.Write8(0, (ushort)address);

            memory.Write8(0x80, 0xFF26); // NR52 powers on the APU first.
            memory.Write8(0x80, 0xFF11);
            memory.Write8(0xF3, 0xFF12);
            memory.Write8(0xF3, 0xFF25);
            memory.Write8(0x77, 0xFF24);
            memory.Write8(0xFC, 0xFF47);

            // Each source nibble is four horizontal pixels, doubled to eight. Repeat each
            // row twice, leaving the high bitplane clear; two nibbles make one 8x8 tile.
            for (int index = 0; index < LogoLength; index++)
            {
                byte value = LogoStart + index < cartridge.Length ? memory.Read8((ushort)(LogoStart + index)) : (byte)0;
                for (int half = 0; half < 2; half++)
                {
                    int nibble = half == 0 ? value >> 4 : value & 15;
                    byte pixels = 0;
                    for (int bit = 3; bit >= 0; bit--)
                        pixels = (byte)((pixels << 2) | ((nibble >> bit & 1) * 3));
                    int tileBase = 0x8010 + (index * 2 + half) * 4;
                    memory.Write8(pixels, (ushort)tileBase);
                    memory.Write8(pixels, (ushort)(tileBase + 2));
                }
            }

            // The registered mark is an 8x8, one-bitplane tile immediately after the logo.
            byte[] registered = { 0x3C, 0x42, 0xB9, 0xA5, 0xB9, 0xA5, 0x42, 0x3C };
            for (int row = 0; row < registered.Length; row++)
                memory.Write8(registered[row], (ushort)(0x8190 + row * 2));
            memory.Write8(0x19, 0x9910);
            for (int column = 0; column < 12; column++)
            {
                memory.Write8((byte)(0x0C - column), (ushort)(0x990F - column));
                memory.Write8((byte)(0x18 - column), (ushort)(0x992F - column));
            }

            memory.Write8(100, 0xFF42);
            memory.Write8(0x91, 0xFF40);
            this.WarnIfLogoInvalid(cartridge);
        }

        private static void PlayTone(MemoryMap memory, byte low)
        {
            memory.Write8(low, 0xFF13);
            memory.Write8(0x87, 0xFF14);
        }

        private void Handoff(MemoryMap memory, Cpu cpu)
        {
            cpu.Registers.A = 0x01;
            cpu.Registers.F = 0xB0;
            cpu.Registers.BC = 0x0013;
            cpu.Registers.DE = 0x00D8;
            cpu.Registers.HL = 0x014D;
            cpu.Registers.SP = 0xFFFE;
            cpu.Registers.PC = 0x0100;
            memory.Write8(0x91, 0xFF40);
            memory.Write8(0, 0xFF42);
            memory.Write8(0, 0xFF43);
            memory.Write8(0xFC, 0xFF47);
            memory.Write8(0, 0xFFFF);
            memory.Write8(0x01, 0xFF0F); // VBlank pending, IME disabled.
        }

        private void WarnIfLogoInvalid(byte[] cartridge)
        {
            int available = Math.Clamp(cartridge.Length - LogoStart, 0, LogoLength);
            int mismatch = -1;
            for (int i = 0; i < available; i++)
            {
                if (cartridge[LogoStart + i] != ExpectedLogo[i]) { mismatch = i; break; }
            }
            if (available == LogoLength && mismatch < 0) return;

            int titleLength = Math.Clamp(cartridge.Length - 0x134, 0, 16);
            string title = titleLength == 0 ? "(title unavailable)" : Encoding.ASCII.GetString(cartridge, 0x134, titleLength).TrimEnd('\0', ' ');
            string detail = mismatch >= 0
                ? $"mismatch at ${LogoStart + mismatch:X4} (expected ${ExpectedLogo[mismatch]:X2}, got ${cartridge[LogoStart + mismatch]:X2})"
                : $"missing/short logo ({available}/{LogoLength} bytes)";
            Trace.TraceWarning($"DMG startup: cartridge '{title}' has {detail}; continuing at $0100.");
        }

        internal readonly record struct StartupState(bool Initialized, bool IsActive, int FramesCompleted);
    }
}
