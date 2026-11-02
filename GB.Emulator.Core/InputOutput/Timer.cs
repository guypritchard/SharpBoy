using System;

namespace GB.Emulator.Core.InputOutput
{
    /// <summary>The divider and programmable timer at FF04–FF07.</summary>
    public sealed class Timer : IMemoryRange
    {
        private ushort divider;
        private byte counter;
        private byte modulo;
        private byte control;
        private int reloadCycles;

        public ushort Start => 0xFF04;
        public ushort End => 0xFF07;

        public byte Read8(ushort location) => location switch
        {
            0xFF04 => (byte)(this.divider >> 8),
            0xFF05 => this.counter,
            0xFF06 => this.modulo,
            0xFF07 => (byte)(0xF8 | this.control),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };

        public void Write8(ushort location, byte value)
        {
            switch (location)
            {
                case 0xFF04:
                    bool wasHigh = this.TimerSignal();
                    this.divider = 0;
                    if (wasHigh) this.IncrementCounter();
                    break;
                case 0xFF05:
                    this.counter = value;
                    this.reloadCycles = 0;
                    break;
                case 0xFF06:
                    this.modulo = value;
                    break;
                case 0xFF07:
                    bool oldSignal = this.TimerSignal();
                    this.control = (byte)(value & 7);
                    if (oldSignal && !this.TimerSignal()) this.IncrementCounter();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(location));
            }
        }

        /// <summary>Advances hardware T-cycles and returns the timer interrupt flag.</summary>
        public byte Step(int cycles)
        {
            if (cycles < 0) throw new ArgumentOutOfRangeException(nameof(cycles));
            byte interrupt = 0;
            while (cycles > 0)
            {
                int untilEdge = int.MaxValue;
                if ((this.control & 4) != 0)
                {
                    int period = 1 << (SelectedBit(this.control) + 1);
                    untilEdge = period - (this.divider & (period - 1));
                }

                int chunk = Math.Min(cycles, Math.Min(untilEdge,
                    this.reloadCycles > 0 ? this.reloadCycles : int.MaxValue));
                this.divider = (ushort)(this.divider + chunk);
                cycles -= chunk;

                if (this.reloadCycles > 0)
                {
                    this.reloadCycles -= chunk;
                    if (this.reloadCycles == 0)
                    {
                        this.counter = this.modulo;
                        interrupt |= 0x04;
                    }
                }

                if (chunk == untilEdge) this.IncrementCounter();
            }
            return interrupt;
        }

        internal void Reset()
        {
            this.divider = 0;
            this.counter = 0;
            this.modulo = 0;
            this.control = 0;
            this.reloadCycles = 0;
        }

        internal TimerState Snapshot() => new(this.divider, this.counter, this.modulo,
            this.control, this.reloadCycles);

        internal void Restore(TimerState state)
        {
            this.divider = state.Divider;
            this.counter = state.Counter;
            this.modulo = state.Modulo;
            this.control = state.Control;
            this.reloadCycles = state.ReloadCycles;
        }

        private bool TimerSignal() => (this.control & 4) != 0 &&
            (this.divider & (1 << SelectedBit(this.control))) != 0;

        private static int SelectedBit(byte control) => (control & 3) switch
        {
            0 => 9,
            1 => 3,
            2 => 5,
            _ => 7
        };

        private void IncrementCounter()
        {
            if (this.reloadCycles > 0) return;
            if (this.counter == 0xFF)
            {
                this.counter = 0;
                this.reloadCycles = 4;
            }
            else this.counter++;
        }

        internal readonly record struct TimerState(ushort Divider, byte Counter, byte Modulo,
            byte Control, int ReloadCycles);
    }
}
