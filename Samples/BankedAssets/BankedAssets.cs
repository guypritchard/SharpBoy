using GB.Sdk;

static class BankedAssetsGame
{
    [GameEntryPoint]
    public static void Start()
    {
        GameBanking.CopyToBuffer(2, 0x4001, 0, 3);
        GameBanking.CopyToBuffer(2, 0x4001, 4, 3);
        GameBanking.CopyToBuffer(2, 0x7FFF, 7, 1);
        GameMemory.WriteByte(0xC100, GameBanking.ReadBufferByte(0));
        GameMemory.WriteByte(0xC101, GameBanking.ReadBufferByte(4));
        GameMemory.WriteByte(0xC102, GameBanking.ReadBufferByte(7));
        if (GameBanking.ReadBufferByte(0) == 0xA1 &&
            GameBanking.ReadBufferByte(4) == 0xA1 && GameBanking.ReadBufferByte(7) == 0xF0)
            GameMemory.WriteByte(0xC110, 0x5A);
        while (true) { }
    }
}
