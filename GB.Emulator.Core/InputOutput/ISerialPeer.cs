namespace GB.Emulator.Core.InputOutput
{
    /// <summary>A device connected to the Game Boy link port.</summary>
    public interface ISerialPeer
    {
        byte ExchangeByte(byte outgoing);
    }
}
