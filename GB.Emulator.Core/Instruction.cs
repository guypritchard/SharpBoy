using System;

namespace GB.Emulator.Core
{
    public class Instruction
    {
        public Instruction(byte value, string name, Action<byte, byte> action, ushort length)
        {
            this.Value = value;
            this.Name = name;
            this.Action = action;
            this.Length = length;
        }

        public short Value { get; private set; }
        public string Name { get; private set; }
        public Action<byte, byte> Action { get; private set; }
        public ushort Length { get; private set; }

        public void Execute(byte p1, byte p2)
        {
            this.Action(p1, p2);
            Cpu.Registers.PC = (ushort)(Cpu.Registers.PC + this.Length);
        }

        public override string ToString()
        {
            return $"{this.Name}";
        }
    }
}
