using GB.Sdk;

static class SemanticGame
{
    [GameEntryPoint]
    static void Start()
    {
        // $C200-$C219 belong exclusively to this fixture; sentinel is written last.
        GameMemory.WriteByte(0xC200, SemanticPure.Zero);
        GameMemory.WriteByte(0xC201, unchecked((byte)SemanticPure.Constant));
        GameMemory.WriteByte(0xC202, SemanticPure.ByteWrap());
        GameMemory.WriteByte(0xC203, unchecked((byte)SemanticPure.SignedWrap()));
        GameMemory.WriteByte(0xC204, unchecked((byte)SemanticPure.WordWrap()));
        GameMemory.WriteByte(0xC205, unchecked((byte)SemanticPure.Carry16()));
        GameMemory.WriteByte(0xC206, unchecked((byte)(SemanticPure.Carry16() - 65535)));
        GameMemory.WriteByte(0xC207, unchecked((byte)SemanticPure.Borrow16()));
        GameMemory.WriteByte(0xC208, unchecked((byte)SemanticPure.Carry32()));
        GameMemory.WriteByte(0xC209, unchecked((byte)(SemanticPure.Carry32() - 2147483520)));
        GameMemory.WriteByte(0xC20A, unchecked((byte)SemanticPure.Borrow32()));
        GameMemory.WriteByte(0xC20B, SemanticPure.Truncate32());
        GameMemory.WriteByte(0xC20C, unchecked((byte)SemanticPure.SignExtend()));
        GameMemory.WriteByte(0xC20D, unchecked((byte)SemanticPure.ZeroExtend()));
        GameMemory.WriteByte(0xC20E, SemanticPure.SignedLess());
        GameMemory.WriteByte(0xC20F, SemanticPure.UnsignedGreater());
        GameMemory.WriteByte(0xC210, SemanticPure.ShortCircuit(false));
        GameMemory.WriteByte(0xC211, SemanticPure.ShortCircuit(true));
        GameMemory.WriteByte(0xC212, SemanticPure.Loop());
        GameMemory.WriteByte(0xC213, unchecked((byte)SemanticPure.NestedCalls()));
        GameMemory.WriteByte(0xC214, SemanticPure.ArgumentOrder());
        GameMemory.WriteByte(0xC215, SemanticPure.LocalCompound());
        GameMemory.WriteByte(0xC216, SemanticPure.Counter);
        GameMemory.WriteByte(0xC217, unchecked((byte)(SemanticPure.Constant - 0x12345622)));
        GameMemory.WriteByte(0xC218, unchecked((byte)(SemanticPure.Constant - 0x12345644)));
        GameMemory.WriteByte(0xC219, 0xA5);
    }
}
