namespace GB.Emulator.Core.InputOutput
{
    /// <summary>Host-offloaded cable. Only the machine owner applies received bytes to SB/SC.</summary>
    public interface IDeferredSerialPeer : ISerialPeer
    {
        bool IsConnected { get; }
        bool IsWaiting { get; }
        void BeginTransfer(byte outgoing, bool internalClock);
        bool TryCompleteTransfer(out byte incoming);
        void CancelTransfer();
    }
}
