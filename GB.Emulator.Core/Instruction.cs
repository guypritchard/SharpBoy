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

        private byte P1 { get; set; }

        private byte P2 { get; set; }

        public void Execute(byte p1, byte p2)
        {
            this.P1 = p1;
            this.P2 = p2;

            this.Action(this.P1, this.P2);

            Cpu.Registers.PC = (ushort)(Cpu.Registers.PC + this.Length);
        }

        public override string ToString()
        {
            if (this.Length == 1)
            {
                return $"{this.Name}";
            }

            return $"{this.Name}, {ByteOp.Concat(this.P1, this.P2):x}";
        }
    }
}
