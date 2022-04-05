using System;
using System.Diagnostics;
namespace GB.Emulator.Core
{
  public class Instruction
  {
    public Instruction(byte value, string name, Action<byte, byte> action, ushort length, bool incrementPc = true)
    {
      this.Value = value;
      this.Name = name;
      this.Action = action;
      this.Length = length;
      this.IncrementPc = incrementPc;
    }

    public bool IncrementPc { get; private set; }
    public short Value { get; private set; }
    public string Name { get; private set; }
    public Action<byte, byte> Action { get; private set; }
    public ushort Length { get; private set; }

    private byte P1 { get; set; }

    private byte P2 { get; set; }

    public void Execute(byte p1, byte p2)
    {
      this.P1 = p1;
      this.P2 = p2; ;

      // Trace.WriteLine(this.Disassemble());
      this.Action(this.P1, this.P2);

      if (this.IncrementPc)
      {
        Cpu.Registers.PC = (ushort)(Cpu.Registers.PC + this.Length);
      }
    }

    public string Disassemble()
    {
      if (this.Length <= 1)
      {
        return $"ROM0:{Cpu.Registers.PC:X4}\t0x{this.Value:X2}\t{this.Name}\t\t\t{this.Length}";
      }

      if (this.Length == 2)
      {
        return $"ROM0:{Cpu.Registers.PC:X4}\t0x{this.Value:X2}\t{this.Name}, 0x{this.P1:X2}\t\t{this.Length}";
      }

      return $"ROM0:{Cpu.Registers.PC:X4}\t0x{this.Value:X2}\t{this.Name}, 0x{ByteOp.Concat(this.P1, this.P2):X2}\t\t{this.Length}";
    }
  }
}
