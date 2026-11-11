using GB.Sdk;

// Two ordinary native SM83 ROMs communicate exclusively through SB/SC, not a network cartridge.
static class LinkLounge
{
    static byte Read(ushort address) { return GameMemory.ReadByte(address); }
    static void Write(ushort address, byte value) { GameMemory.WriteByte(address, value); }
    static void BeginDraw() { GameDisplay.WaitForNextVBlank(); Write(0xFF40, 0); }
    static void EndDraw() { Write(0xFF40, 0x91); }
    [GameEntryPoint]
    public static void Main()
    {
        GameDisplay.InitializeBackground();
        BeginDraw();
        GameText.Write(0x9821, "LINK LOUNGE");
        GameText.Write(0x9861, "CONNECT YOUR CABLE");
        GameText.Write(0x98A1, "START: PLAYER ONE");
        GameText.Write(0x98E1, "SELECT: PLAYER TWO");
        GameText.Write(0x9941, "A: ADD A POINT");
        GameText.Write(0x9981, "YOU:"); GameText.Write(0x99E1, "FRIEND:");
        EndDraw();
        byte mode = 0;
        while (mode == 0)
        {
            Write(0xFF00, 0x10); byte buttons = Read(0xFF00);
            if ((buttons & 8) == 0) mode = 0x81;
            if ((buttons & 4) == 0) mode = 0x80;
        }
        BeginDraw();
        GameText.Write(0x9861, "CABLE READY       ");
        GameText.Write(0x98A1, "                  ");
        GameText.Write(0x98E1, "                  ");
        if (mode == 0x81) GameText.Write(0x98A1, "PLAYER ONE");
        if (mode == 0x80) GameText.Write(0x98A1, "PLAYER TWO");
        EndDraw();
        byte score = 0, previous = 1;
        while (true)
        {
            Write(0xFF00, 0x10); byte pressed = (byte)(Read(0xFF00) & 1);
            if (pressed == 0 && previous != 0) { score++; if (score == 100) score = 0; }
            previous = pressed;
            Write(0xFF01, score); Write(0xFF02, mode);
            while ((Read(0xFF02) & 0x80) != 0) { }
            byte friend = Read(0xFF01);
            BeginDraw();
            GameText.Write(0x9986, "   "); GameText.Write(0x99E9, "   ");
            GameTextNumbers.WriteDecimal(0x9986, score);
            if (friend != 255) GameTextNumbers.WriteDecimal(0x99E9, friend);
            if (friend == 255) GameText.Write(0x99E9, "---");
            EndDraw();
        }
    }
}
