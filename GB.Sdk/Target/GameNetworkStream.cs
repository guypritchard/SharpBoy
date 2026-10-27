// Compile alongside GameNetwork.cs for version-2 TCP socket-offload cartridges.
// Preserve unread bytes: TCP may split OR coalesce application records arbitrarily.
static class GameNetworkStream
{
    static ushort StreamAvailable, StreamOffset;
    public static bool Send(ushort source, ushort count)
    {
        if (count == 0 || count > 1024) return false;
        GameNetwork.CopyBytes(source, 0xA100, count);
        GameNetwork.WriteWord(0xA005, count);
        return GameNetwork.Execute(2) == 0;
    }
    public static bool Receive(ushort destination, ushort count)
    {
        while (count != 0)
        {
            if (StreamAvailable == 0)
            {
                if (GameNetwork.Execute(3) != 0) return false;
                StreamAvailable = GameNetwork.ReadWord(0xA007); StreamOffset = 0;
                if (StreamAvailable == 0 || StreamAvailable > 1024) return false;
            }
            GameNetwork.Write8(destination, GameNetwork.Read8((ushort)(0xA500 + StreamOffset)));
            destination++; StreamOffset++; StreamAvailable--; count--;
            if (StreamAvailable == 0 && GameNetwork.Execute(5) != 0) return false;
        }
        return true;
    }
}
