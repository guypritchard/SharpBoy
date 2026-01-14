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

        public override string ToString()
        {
            return Name;
        }

        public void Execute(byte p1, byte p2)
    {
      this.P1 = p1;
      this.P2 = p2;

      Trace.WriteLine(this.Disassemble());
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
        string operand = $"0x{this.P1:X2}";
        if (this.Value is 0xE0 or 0xF0)
        {
          ushort address = (ushort)(0xFF00 + this.P1);
          string label = GetIoLabel(address);
          operand = label ?? $"0x{address:X4}";
        }

        string formatted = FormatWithOperand(this.Name, operand);
        return $"ROM0:{Cpu.Registers.PC:X4}\t0x{this.Value:X2}\t{formatted}\t\t\t{this.Length}";
      }

      ushort immediate = ByteOp.Concat(this.P1, this.P2);
      string immediateOperand = $"0x{immediate:X4}";
      string immediateLabel = GetIoLabel(immediate);
      if (immediateLabel != null)
      {
        immediateOperand = this.Value == 0xEA
          ? immediateLabel
          : $"0x{immediate:X4} ({immediateLabel})";
      }

      string immediateFormatted = FormatWithOperand(this.Name, immediateOperand);
      return $"ROM0:{Cpu.Registers.PC:X4}\t0x{this.Value:X2}\t{immediateFormatted}\t\t\t{this.Length}";
    }

    internal static string FormatWithOperand(string name, string operand)
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        return operand;
      }

      if (name.Contains("d8", StringComparison.Ordinal))
      {
        return name.Replace("d8", operand, StringComparison.Ordinal);
      }

      if (name.Contains("a8", StringComparison.Ordinal))
      {
        return name.Replace("a8", operand, StringComparison.Ordinal);
      }

      if (name.Contains("s8", StringComparison.Ordinal))
      {
        return name.Replace("s8", operand, StringComparison.Ordinal);
      }

      if (name.Contains("d16", StringComparison.Ordinal))
      {
        return name.Replace("d16", operand, StringComparison.Ordinal);
      }

      if (name.Contains("a16", StringComparison.Ordinal))
      {
        return name.Replace("a16", operand, StringComparison.Ordinal);
      }

      return $"{name}, {operand}";
    }

    private static string GetIoLabel(ushort address)
    {
      return address switch
      {
        0xFF44 => "LY",
        0xFF0F => "IF",
        0xFFFF => "IE",
        _ => null
      };
    }
  }
}
