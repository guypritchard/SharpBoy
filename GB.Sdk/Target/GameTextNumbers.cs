using GB.Sdk;

// Compile this source alongside a game. No runtime library or managed allocation is needed.
namespace GB.Sdk;

/// <summary>Reusable constrained-C# decimal and IPv4 tile drawing.</summary>
public static class GameTextNumbers
{
    public static ushort WriteDecimal(ushort destination, byte value)
    {
        byte hundreds = 0;
        byte tens = 0;
        while (value >= 100) { value -= 100; hundreds++; }
        if (hundreds != 0) { GameMemory.WriteByte(destination, (byte)(48 + hundreds)); destination++; }
        while (value >= 10) { value -= 10; tens++; }
        if (hundreds != 0 || tens != 0) { GameMemory.WriteByte(destination, (byte)(48 + tens)); destination++; }
        GameMemory.WriteByte(destination, (byte)(48 + value));
        return (ushort)(destination + 1);
    }

    public static void WriteIpv4(ushort destination, ushort source)
    {
        byte count = 0;
        while (count < 4)
        {
            destination = WriteDecimal(destination, GameMemory.ReadByte(source));
            source++; count++;
            if (count < 4) { GameText.Write(destination, "."); destination++; }
        }
    }
}
