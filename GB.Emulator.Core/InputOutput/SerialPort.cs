#nullable enable
using System;

namespace GB.Emulator.Core.InputOutput
{
    /// <summary>DMG serial data (SB) and control (SC) registers.</summary>
    public sealed class SerialPort : IMemoryRange
    {
        private const int InternalTransferCycles = 4096;
        private byte data;
        private byte control;
        private int remainingCycles;

        public ushort Start => 0xFF01;
        public ushort End => 0xFF02;
        public bool IsTransferPending => (this.control & 0x80) != 0;
        public bool UsesExternalClock => (this.control & 0x01) == 0;
        public ISerialPeer? Peer { get; set; }

        /// <summary>Raised when a byte exchange completes and IF bit 3 must be set.</summary>
        public event EventHandler? InterruptRequested;

        public byte Read8(ushort location) => location switch
        {
            0xFF01 => this.data,
            0xFF02 => (byte)(this.control | 0x7C),
            _ => throw new ArgumentOutOfRangeException(nameof(location))
        };

        public void Write8(ushort location, byte value)
        {
            switch (location)
            {
                case 0xFF01:
                    this.data = value;
                    break;
                case 0xFF02:
                    this.control = (byte)(value & 0x83);
                    this.remainingCycles = this.IsTransferPending && !this.UsesExternalClock
                        ? InternalTransferCycles : 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(location));
            }
        }

        /// <summary>Advance the internal 8192 Hz DMG clock by CPU clock cycles.</summary>
        public void Step(int cycles)
        {
            if (!this.IsTransferPending || this.UsesExternalClock) return;
            this.remainingCycles -= cycles;
            if (this.remainingCycles > 0) return;

            byte incoming = this.Peer?.ExchangeByte(this.data) ?? 0xFF;
            this.Complete(incoming);
        }

        /// <summary>Clock one byte from an external link partner. Returns the byte sent by this Game Boy.</summary>
        public byte ClockExternal(byte incoming)
        {
            if (!this.IsTransferPending || !this.UsesExternalClock)
                throw new InvalidOperationException("The serial port is not waiting for an external clock.");

            byte outgoing = this.data;
            this.Complete(incoming);
            return outgoing;
        }

        private void Complete(byte incoming)
        {
            this.data = incoming;
            this.control &= 0x7F;
            this.remainingCycles = 0;
            this.InterruptRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            this.data = 0;
            this.control = 0;
            this.remainingCycles = 0;
        }

        internal SerialPortState Snapshot() => new(this.data, this.control, this.remainingCycles);

        internal void Restore(SerialPortState state)
        {
            this.data = state.Data;
            this.control = state.Control;
            this.remainingCycles = state.RemainingCycles;
        }

        internal readonly record struct SerialPortState(byte Data, byte Control, int RemainingCycles);
    }
}
