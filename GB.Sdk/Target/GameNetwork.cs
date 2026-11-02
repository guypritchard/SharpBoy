using GB.Sdk;

// Compile this shared source into the ROM. It has no host-side network implementation.
static class GameNetwork
{
    public static byte Read8(ushort address) { return GameMemory.ReadByte(address); }
    public static void Write8(ushort address, byte value) { GameMemory.WriteByte(address, value); }
    public static byte Execute(byte command)
    {
        Write8(0xA003, command);
        while ((Read8(0xA002) & 2) != 0) { }
        return Read8(0xA004);
    }
    public static void CopyBytes(ushort source, ushort destination, ushort count)
    {
        while (count != 0) { Write8(destination, Read8(source)); source++; destination++; count--; }
    }
    public static ushort ReadWord(ushort address)
    {
        ushort value = Read8((ushort)(address + 1));
        byte bit = 0;
        while (bit < 8) { value += value; bit++; }
        return (ushort)(value + Read8(address));
    }
    public static void WriteWord(ushort address, ushort value)
    {
        Write8(address, (byte)value);
        byte high = 0;
        while (value > 255) { value -= 256; high++; }
        Write8((ushort)(address + 1), high);
    }
    public static bool OpenConfigured()
    {
        if (Read8(0xA000) != 78 || Read8(0xA001) != 2 || (Read8(0xA009) & 3) != 3) return false;
        Write8(0xA00A, Read8(0xA031)); // 0=UDP, 1=TCP; host-provided boot configuration
        Write8(0xA010, 0); Write8(0xA011, 0); // ephemeral local UDP port
        CopyBytes(0xA032, 0xA014, 4); CopyBytes(0xA036, 0xA012, 2);
        return Execute(1) == 0;
    }
    public static bool Send(ushort source, byte count)
    {
        CopyBytes(source, 0xA100, count); Write8(0xA005, count); Write8(0xA006, 0);
        return Execute(2) == 0;
    }
    // Stop-and-wait application framing: UDP must contain exactly one record;
    // TCP can deliver a record across several reads. Never assume a TCP read is a packet.
    public static bool Receive(ushort destination, byte count)
    {
        byte total = 0;
        while (total < count)
        {
            if (Execute(3) != 0) return false;
            ushort length = ReadWord(0xA007);
            if (length == 0 || length > (ushort)(count - total)) return false;
            if (Read8(0xA00A) == 0)
            {
                if (length != count || ReadWord(0xA018) != ReadWord(0xA036)) return false;
                ushort index = 0;
                while (index < 4)
                {
                    if (Read8((ushort)(0xA01A + index)) != Read8((ushort)(0xA032 + index))) return false;
                    index++;
                }
            }
            CopyBytes(0xA500, (ushort)(destination + total), length);
            total = (byte)(total + length);
            if (Execute(5) != 0) return false;
        }
        return true;
    }
}
