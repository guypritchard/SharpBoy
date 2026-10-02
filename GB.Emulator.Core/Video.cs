#nullable enable
using System;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    /// <summary>The LCD clock and video memory as seen by the Game Boy hardware.</summary>
    public sealed class Video
    {
        public const int Width = 160;
        public const int Height = 144;

        private readonly Lcd lcd;

        public event EventHandler? FrameReady;

        public Video(Lcd lcd)
        {
            this.lcd = lcd;
        }

        public byte Step(int cycles = 4)
        {
            byte interrupts = this.lcd.Step(cycles);
            if ((interrupts & 0x01) != 0)
            {
                this.FrameReady?.Invoke(this, EventArgs.Empty);
            }

            return interrupts;
        }

        internal VideoState CaptureState(MemoryMap memory) => new(memory.Snapshot());
    }
}
