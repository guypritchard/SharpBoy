// This exact file is compiled both by the host tests and into the cartridge.
// No host APIs, hardware intrinsics, or target-specific branches are used here.
public static class SemanticPure
{
    public static byte Zero;
    public static int Constant = 0x12345678;
    public static byte Counter;

    public static byte ByteWrap() => unchecked((byte)(255 + 2));
    public static sbyte SignedWrap() => unchecked((sbyte)255);
    public static ushort WordWrap() => unchecked((ushort)(65535 + 2));
    public static int Carry16() => (ushort)65535 + 1;
    public static int Borrow16() => (ushort)0 - 1;
    public static int Carry32()
    {
        int value = 0x7fffffff;
        return value + 1; // default unchecked build; wraps to int.MinValue
    }
    public static int Borrow32()
    {
        int value = -2147483647 - 1;
        return value - 1; // default unchecked build; wraps to int.MaxValue
    }
    public static byte Truncate32() => unchecked((byte)(0x12345678 + 0x100));
    public static int SignExtend() => (sbyte)-1;
    public static int ZeroExtend() => (byte)255;

    public static byte SignedLess()
    {
        short left = -1;
        if (left < (short)1 && (sbyte)-1 < (byte)1) return 1;
        return 0;
    }

    public static byte UnsignedGreater()
    {
        ushort left = 65535;
        short right = -1;
        if (left > right && (byte)255 > (sbyte)-1) return 1;
        return 0;
    }

    static bool Next()
    {
        Counter++;
        return true;
    }

    public static byte ShortCircuit(bool left)
    {
        Counter = 0;
        bool andResult = left && Next();
        bool orResult = left || Next();
        if (andResult != left || !orResult) return 0xff;
        byte result = Counter;
        if (andResult) result += 0x10;
        if (orResult) result += 0x20;
        return result;
    }

    public static byte Loop()
    {
        byte sum = 0;
        byte i = 0;
        while (i < 8)
        {
            i++;
            if (i == 2) continue;
            if (i == 6) break;
            sum += i;
        }
        return sum;
    }

    static int First(int value) => value + 1;
    static int Second(int value) => value - 3;
    static int Combine(int left, int right) => left - right;

    public static int NestedCalls() => Combine(First(10), Second(4));

    static int OrderedFirst()
    {
        Counter++;
        return Counter;
    }

    static int OrderedSecond()
    {
        Counter++;
        return Counter;
    }

    public static byte ArgumentOrder()
    {
        Counter = 0;
        return unchecked((byte)Combine(OrderedFirst(), OrderedSecond()));
    }

    public static byte LocalCompound()
    {
        byte local = 255;
        local += 2;
        local ^= 0x80;
        return local;
    }
}
