using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Hosting;

public sealed partial class EmulationHost
{
    /// <summary>Attach on the host's command thread; adapters never access live machine state.</summary>
    public void AttachSerialPeer(ISerialPeer? peer)
    {
        this.EnsureCommandAllowed();
        this.Debugging.ClearHistory();
        this.Machine.Serial.Peer = peer;
    }
    public bool IsWaitingForSerialPeer => this.Machine.Serial.IsWaitingForPeer;
}
