namespace GB.Emulator.Core.InputOutput
{
    public interface IMemoryRange
    {
        public ushort Start { get; }
        public ushort End { get; }

        void Write8(ushort location, byte value);

        byte Read8(ushort location);
    }
}
