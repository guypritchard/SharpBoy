namespace GB.Emulator.Core.InputOutput
{
    /// <summary>A peripheral whose registers can be inspected without performing a bus transaction.</summary>
    public interface IInspectableMemoryRange : IMemoryRange
    {
        byte Peek(ushort address);
    }
}
