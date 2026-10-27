using GB.Sdk;

// Native Conway B3/S23, 32x24 cells, one-cell halo routed over TCP by the coordinator.
static class NetworkLife
{
    const ushort Packet = 0xC080, Halo = 0xC100, Output = 0xC480, Border = 0xC800;
    static void DrawRow(byte row, ushort rowOffset)
    {
        ushort tileRow = 0; byte remaining = row;
        while (remaining >= 2) { remaining -= 2; tileRow += 256; }
        ushort address = (ushort)(0x8000 + tileRow);
        if ((row & 1) != 0) address += 8;
        byte column = 0;
        while (column < 32)
        {
            byte alive = GameNetwork.Read8((ushort)(Output + 16 + rowOffset + column));
            byte shade = 0;
            if (alive != 0) shade = 3;
            byte mask = 240;
            if ((column & 1) != 0) mask = 15;
            byte low = 0, high = 0;
            if ((shade & 1) != 0) low = mask;
            if ((shade & 2) != 0) high = mask;
            byte line = 0;
            while (line < 8)
            {
                ushort target = (ushort)(address + line);
                GameNetwork.Write8(target, (byte)((GameNetwork.Read8(target) & (255 - mask)) | low));
                GameNetwork.Write8((ushort)(target + 1), (byte)((GameNetwork.Read8((ushort)(target + 1)) & (255 - mask)) | high));
                line += 2;
            }
            if ((column & 1) != 0) address += 16;
            column++;
        }
    }
    [GameEntryPoint]
    public static void Start()
    {
        GameNetwork.Write8(0xC060, 0);
        GameDisplay.InitializeBackground();
        if (!GameNetwork.OpenConfigured()) { GameNetwork.Write8(0xC060, 255); return; }
        GameNetwork.Write8(Output, 71); GameNetwork.Write8(Output + 1, 66);
        GameNetwork.Write8(Output + 2, 76); GameNetwork.Write8(Output + 3, 50);
        GameNetwork.Write8(Output + 4, GameNetwork.Read8(0xA030));
        GameNetwork.Write8(Output + 5, GameNetwork.Read8(0xA03F));
        GameNetwork.Write8(Output + 14, 0);
        if (!GameNetworkStream.Send(Output, 16)) { GameNetwork.Write8(0xC060, 255); return; }
        ushort generation = 1;
        while (true)
        {
            if (!GameNetworkStream.Receive(Packet, 16) || GameNetwork.Read8(Packet) != 71 ||
                GameNetwork.Read8(Packet + 1) != 66 || GameNetwork.Read8(Packet + 2) != 76 ||
                GameNetwork.Read8(Packet + 3) != 50 || GameNetwork.Read8(Packet + 4) != GameNetwork.Read8(0xA030) ||
                GameNetwork.Read8(Packet + 5) != GameNetwork.Read8(0xA03F) ||
                GameNetwork.ReadWord(Packet + 6) != generation)
            { GameNetwork.Write8(0xC060, 255); return; }
            if (GameNetwork.Read8(Packet + 14) == 3)
            {
                if (GameNetwork.Execute(4) == 0) GameNetwork.Write8(0xC060, 1);
                else GameNetwork.Write8(0xC060, 255);
                return;
            }
            ushort payload = 116;
            if (generation == 1) payload = 884;
            if (GameNetwork.Read8(Packet + 14) != 1 || GameNetwork.ReadWord(Packet + 8) != 32 ||
                GameNetwork.ReadWord(Packet + 10) != 24 || GameNetwork.ReadWord(Packet + 12) != payload)
            { GameNetwork.Write8(0xC060, 255); return; }
            if (generation == 1)
            {
                if (!GameNetworkStream.Receive(Halo, 884)) { GameNetwork.Write8(0xC060, 255); return; }
            }
            else
            {
                // Keep the prior interior in guest WRAM; replace only the 116-cell boundary.
                if (!GameNetworkStream.Receive(Border, 116)) { GameNetwork.Write8(0xC060, 255); return; }
                GameNetwork.CopyBytes(Border, Halo, 34);
                ushort edge = Border + 34, left = Halo + 34;
                byte borderRow = 0;
                while (borderRow < 24)
                {
                    GameNetwork.Write8(left, GameNetwork.Read8(edge));
                    GameNetwork.Write8((ushort)(left + 33), GameNetwork.Read8((ushort)(edge + 1)));
                    edge += 2; left += 34; borderRow++;
                }
                GameNetwork.CopyBytes(edge, Halo + 850, 34);
            }
            ushort input = Halo + 35, output = Output + 16;
            byte y = 0;
            while (y < 24)
            {
                byte x = 0;
                while (x < 32)
                {
                    byte count = (byte)(GameNetwork.Read8((ushort)(input - 35)) + GameNetwork.Read8((ushort)(input - 34)) +
                        GameNetwork.Read8((ushort)(input - 33)) + GameNetwork.Read8((ushort)(input - 1)) +
                        GameNetwork.Read8((ushort)(input + 1)) + GameNetwork.Read8((ushort)(input + 33)) +
                        GameNetwork.Read8((ushort)(input + 34)) + GameNetwork.Read8((ushort)(input + 35)));
                    byte alive = 0;
                    if (count == 3 || (count == 2 && GameNetwork.Read8(input) == 1)) alive = 1;
                    GameNetwork.Write8(output, alive); input++; output++; x++;
                }
                input += 2; y++;
            }
            GameDisplay.WaitForNextVBlank(); GameNetwork.Write8(0xFF40, 0);
            byte row = 0; ushort offset = 0;
            while (row < 24) { DrawRow(row, offset); row++; offset += 32; }
            GameNetwork.Write8(0xFF40, 0x91);
            GameNetwork.Write8(Output + 14, 2); GameNetwork.WriteWord(Output + 6, generation);
            GameNetwork.WriteWord(Output + 8, 32); GameNetwork.WriteWord(Output + 10, 24);
            GameNetwork.WriteWord(Output + 12, 768);
            if (!GameNetworkStream.Send(Output, 784)) { GameNetwork.Write8(0xC060, 255); return; }
            ushort previous = Output + 16, interior = Halo + 35;
            byte retainedRow = 0;
            while (retainedRow < 24)
            { GameNetwork.CopyBytes(previous, interior, 32); previous += 32; interior += 34; retainedRow++; }
            generation++;
        }
    }
}
