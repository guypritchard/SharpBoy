namespace GB.Emulator.Core.InputOutput
{
    /// <summary>Connects an internally clocked Game Boy to an externally clocked one.</summary>
    public sealed class SerialLinkCable : ISerialPeer
    {
        private readonly SerialPort otherEnd;

        public SerialLinkCable(SerialPort otherEnd) => this.otherEnd = otherEnd;

        public byte ExchangeByte(byte outgoing) =>
            this.otherEnd.IsTransferPending && this.otherEnd.UsesExternalClock
                ? this.otherEnd.ClockExternal(outgoing)
                : (byte)0xFF;
    }
}
