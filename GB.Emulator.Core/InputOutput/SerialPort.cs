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
        private byte armedData;

        public ushort Start => 0xFF01;
        public ushort End => 0xFF02;
        public bool IsTransferPending => (this.control & 0x80) != 0;
        public bool UsesExternalClock => (this.control & 0x01) == 0;
        private ISerialPeer? peer;
        public ISerialPeer? Peer
        {
            get => this.peer;
            set
            {
                (this.peer as IDeferredSerialPeer)?.CancelTransfer();
                this.peer = value;
                if (this.IsTransferPending && value is IDeferredSerialPeer deferred)
                { this.armedData = this.data; deferred.BeginTransfer(this.data, !this.UsesExternalClock); }
            }
        }
        public bool IsWaitingForPeer => this.IsTransferPending && this.peer is IDeferredSerialPeer { IsConnected: true, IsWaiting: true } &&
            !this.UsesExternalClock && this.remainingCycles <= 0;

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
                    // Games re-arm external SC every menu frame. Keep the outstanding
                    // rendezvous (including a just-arrived reply) when nothing changed.
                    if ((value & 0x83) == 0x80 && this.control == 0x80 && this.data == this.armedData &&
                        this.peer is IDeferredSerialPeer { IsConnected: true }) return;
                    (this.peer as IDeferredSerialPeer)?.CancelTransfer();
                    this.control = (byte)(value & 0x83);
                    this.remainingCycles = this.IsTransferPending && !this.UsesExternalClock
                        ? InternalTransferCycles : 0;
                    if (this.IsTransferPending && this.peer is IDeferredSerialPeer deferred)
                    { this.armedData = this.data; deferred.BeginTransfer(this.data, !this.UsesExternalClock); }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(location));
            }
        }

        /// <summary>Advance the internal 8192 Hz DMG clock by CPU clock cycles.</summary>
        public void Step(int cycles)
        {
            if (!this.IsTransferPending) return;
            if (this.UsesExternalClock) { this.Pump(); return; }
            this.remainingCycles -= cycles;
            if (this.remainingCycles > 0) return;

            if (this.peer is IDeferredSerialPeer { IsConnected: true } network)
            {
                if (network.TryCompleteTransfer(out byte received)) this.Complete(received);
                return;
            }
            byte incoming = this.peer is IDeferredSerialPeer ? (byte)0xFF : this.Peer?.ExchangeByte(this.data) ?? 0xFF;
            this.Complete(incoming);
        }

        /// <summary>Poll without advancing guest time; safe while the network clock is parked.</summary>
        public void Pump()
        {
            if (this.IsTransferPending && (this.UsesExternalClock || this.remainingCycles <= 0) &&
                this.peer is IDeferredSerialPeer { IsConnected: true } network && network.TryCompleteTransfer(out byte value))
                this.Complete(value);
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
            (this.peer as IDeferredSerialPeer)?.CancelTransfer();
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
