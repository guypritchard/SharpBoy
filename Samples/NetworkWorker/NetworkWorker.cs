using GB.Sdk;

static class NetworkWorker
{
    const ushort Packet = 0xC080;
    [GameEntryPoint]
    public static void Start()
    {
        GameNetwork.Write8(0xC060, 0);
        if (!GameNetwork.OpenConfigured()) { GameNetwork.Write8(0xC060, 255); return; }
        GameNetwork.Write8(Packet, 71); GameNetwork.Write8(Packet + 1, 66);
        GameNetwork.Write8(Packet + 2, 87); GameNetwork.Write8(Packet + 3, 49);
        GameNetwork.Write8(Packet + 4, GameNetwork.Read8(0xA030));
        GameNetwork.Write8(Packet + 5, 0); // HELLO
        if (!GameNetwork.Send(Packet, 16)) { GameNetwork.Write8(0xC060, 255); return; }
        while (true)
        {
            if (!GameNetwork.Receive(Packet, 16)) { GameNetwork.Write8(0xC060, 255); return; }
            if (GameNetwork.Read8(Packet) != 71 || GameNetwork.Read8(Packet + 1) != 66 ||
                GameNetwork.Read8(Packet + 2) != 87 || GameNetwork.Read8(Packet + 3) != 49 ||
                GameNetwork.Read8(Packet + 4) != GameNetwork.Read8(0xA030)) { GameNetwork.Write8(0xC060, 255); return; }
            if (GameNetwork.Read8(Packet + 5) == 3) // STOP
            {
                if (GameNetwork.Execute(4) == 0) GameNetwork.Write8(0xC060, 1);
                else GameNetwork.Write8(0xC060, 255);
                return;
            }
            if (GameNetwork.Read8(Packet + 5) != 1) { GameNetwork.Write8(0xC060, 255); return; }
            ushort value = GameNetwork.ReadWord(Packet + 8);
            ushort count = GameNetwork.ReadWord(Packet + 10);
            ushort sum = 0;
            while (count != 0) { sum = (ushort)(sum + value); value++; count--; }
            GameNetwork.WriteWord(Packet + 12, sum);
            GameNetwork.Write8(Packet + 5, 2); // RESULT; job id and operands echoed unchanged
            if (!GameNetwork.Send(Packet, 16)) { GameNetwork.Write8(0xC060, 255); return; }
        }
    }
}
