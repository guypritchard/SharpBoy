using GB.Sdk;

static class ArithmeticGame
{
    static int Seed = 35;
    static byte Uninitialized;

    [GameEntryPoint]
    static void Start()
    {
        int value = Add(Seed, 7);
        GameMemory.WriteByte(0xC100, (byte)value);
        GameMemory.WriteByte(0xC101, 0xA5);
    }

    static int Add(int lhs, int rhs) => lhs + rhs;
}
