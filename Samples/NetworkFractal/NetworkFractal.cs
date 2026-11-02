using GB.Sdk;

// Fixed-point Mandelbrot: Q5 coordinates, exact integer squares, 32x24 pixels per node.
// The host only sends tile coordinates and collects these native iteration counts.
static class NetworkFractal
{
    const ushort Packet = 0xC080, Squares = 0xC400;
    static byte FractalLimit;
    static ushort FractalSquare(short value)
    {
        if (value < 0) value = (short)-value;
        ushort address = (ushort)(Squares + value + value);
        return GameNetwork.ReadWord(address);
    }
    static short FractalScale(short value)
    {
        bool negative = value < 0;
        if (negative) value = (short)-value;
        short result = 0;
        if (value >= 8192) { value -= 8192; result += 256; }
        if (value >= 4096) { value -= 4096; result += 128; }
        if (value >= 2048) { value -= 2048; result += 64; }
        if (value >= 1024) { value -= 1024; result += 32; }
        if (value >= 512) { value -= 512; result += 16; }
        if (value >= 256) { value -= 256; result += 8; }
        if (value >= 128) { value -= 128; result += 4; }
        if (value >= 64) { value -= 64; result += 2; }
        if (value >= 32) result++;
        if (negative) return (short)-result;
        return result;
    }
    static byte FractalPixel(short real, short imaginary)
    {
        short x = 0, y = 0;
        byte iteration = 0;
        while (iteration < FractalLimit)
        {
            if (x < -64 || x > 64 || y < -64 || y > 64) return iteration;
            ushort xx = FractalSquare(x), yy = FractalSquare(y);
            if (xx + yy > 4096) return iteration;
            short nextY = (short)(FractalScale((short)(FractalSquare((short)(x + y)) - xx - yy)) + imaginary);
            x = (short)(FractalScale((short)(xx - yy)) + real);
            y = nextY; iteration++;
        }
        return iteration;
    }
    static void FractalDrawRow(byte row)
    {
        GameDisplay.WaitForNextVBlank(); GameNetwork.Write8(0xFF40, 0);
        ushort tileRow = 0; byte remaining = row;
        while (remaining >= 2) { remaining -= 2; tileRow += 256; }
        ushort address = (ushort)(0x8000 + tileRow);
        if ((row & 1) != 0) address += 8;
        byte column = 0;
        while (column < 32)
        {
            byte iterations = GameNetwork.Read8((ushort)(Packet + 16 + column));
            byte shade = (byte)(iterations & 3);
            if (iterations == FractalLimit) shade = 3;
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
        GameNetwork.Write8(0xFF40, 0x91);
    }
    [GameEntryPoint]
    public static void Start()
    {
        GameNetwork.Write8(0xC060, 0);
        FractalLimit = GameNetwork.Read8(0xA038);
        if (FractalLimit < 1 || FractalLimit > 64) { GameNetwork.Write8(0xC060, 255); return; }
        GameDisplay.InitializeBackground();
        ushort square = 0, odd = 1, cursor = Squares;
        byte index = 0;
        while (index < 129)
        {
            GameNetwork.WriteWord(cursor, square);
            square = (ushort)(square + odd); odd += 2; cursor += 2; index++;
        }
        if (!GameNetwork.OpenConfigured()) { GameNetwork.Write8(0xC060, 255); return; }
        GameNetwork.Write8(Packet, 71); GameNetwork.Write8(Packet + 1, 66);
        GameNetwork.Write8(Packet + 2, 87); GameNetwork.Write8(Packet + 3, 49);
        GameNetwork.Write8(Packet + 4, GameNetwork.Read8(0xA030)); GameNetwork.Write8(Packet + 5, 0);
        if (!GameNetwork.Send(Packet, 16) || !GameNetwork.Receive(Packet, 16)) { GameNetwork.Write8(0xC060, 255); return; }
        if (GameNetwork.Read8(Packet) != 71 || GameNetwork.Read8(Packet + 1) != 66 ||
            GameNetwork.Read8(Packet + 2) != 87 || GameNetwork.Read8(Packet + 3) != 49 ||
            GameNetwork.Read8(Packet + 4) != GameNetwork.Read8(0xA030) || GameNetwork.Read8(Packet + 5) != 4 ||
            GameNetwork.ReadWord(Packet + 12) < 1 || GameNetwork.ReadWord(Packet + 12) > 64 ||
            GameNetwork.ReadWord(Packet + 14) < 1 || GameNetwork.ReadWord(Packet + 14) > 64 ||
            GameNetwork.ReadWord(Packet + 8) >= GameNetwork.ReadWord(Packet + 12) ||
            GameNetwork.ReadWord(Packet + 10) >= GameNetwork.ReadWord(Packet + 14))
        { GameNetwork.Write8(0xC060, 255); return; }
        byte tileX = GameNetwork.Read8(Packet + 8), tileY = GameNetwork.Read8(Packet + 10);
        byte columns = GameNetwork.Read8(Packet + 12), rows = GameNetwork.Read8(Packet + 14);
        short startX = -72, startY = -36;
        byte remainderX = 0, remainderY = 0;
        byte offset = 0;
        while (offset < tileX)
        {
            byte step = 0;
            while (step < 32) { remainderX += 3; while (remainderX >= columns) { remainderX -= columns; startX++; } step++; }
            offset++;
        }
        offset = 0;
        while (offset < tileY)
        {
            byte step = 0;
            while (step < 24) { remainderY += 3; while (remainderY >= rows) { remainderY -= rows; startY++; } step++; }
            offset++;
        }
        GameNetwork.Write8(Packet + 2, 70); // GBF1 rows
        GameNetwork.Write8(Packet + 6, tileX); GameNetwork.Write8(Packet + 7, tileY);
        byte row = 0;
        while (row < 24)
        {
            byte column = 0;
            short real = startX;
            byte errorX = remainderX;
            ushort work = 0;
            while (column < 32)
            {
                byte value = FractalPixel(real, startY);
                GameNetwork.Write8((ushort)(Packet + 16 + column), value); work += value; column++;
                errorX += 3; while (errorX >= columns) { errorX -= columns; real++; }
            }
            GameNetwork.Write8(Packet + 5, row); GameNetwork.WriteWord(Packet + 8, work);
            FractalDrawRow(row);
            if (!GameNetwork.Send(Packet, 48)) { GameNetwork.Write8(0xC060, 255); return; }
            // Row acknowledgements provide bounded back-pressure for a visible demo.
            if (!GameNetwork.Receive(Packet, 16) || GameNetwork.Read8(Packet) != 71 ||
                GameNetwork.Read8(Packet + 1) != 66 || GameNetwork.Read8(Packet + 2) != 70 ||
                GameNetwork.Read8(Packet + 3) != 49 || GameNetwork.Read8(Packet + 4) != GameNetwork.Read8(0xA030) ||
                GameNetwork.Read8(Packet + 5) != row || GameNetwork.Read8(Packet + 6) != tileX ||
                GameNetwork.Read8(Packet + 7) != tileY || GameNetwork.ReadWord(Packet + 8) != work)
            { GameNetwork.Write8(0xC060, 255); return; }
            row++;
            remainderY += 3; while (remainderY >= rows) { remainderY -= rows; startY++; }
        }
        if (GameNetwork.Execute(4) == 0) GameNetwork.Write8(0xC060, 1);
        else GameNetwork.Write8(0xC060, 255);
    }
}
