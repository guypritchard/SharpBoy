
using GB.Emulator.Core.InputOutput;

public class Interrupt : IMemoryRange
{
    public Interrupt()
    {
        this.Start = 0xFFFF;
        this.End = 0xFFFF;
    }

    public ushort Start
    {
        get; private set;
    }

    public ushort End
    {
        get; private set;
    }

    private byte interruptFlags = 0x00;

    public void Write8(ushort location, byte value)
    {
        this.interruptFlags = value;
    }

    public byte Read8(ushort location)
    {
        return this.interruptFlags;
    }

    internal byte Snapshot()
    {
        return this.interruptFlags;
    }

    internal void Restore(byte snapshot)
    {
        this.interruptFlags = snapshot;
    }
}
