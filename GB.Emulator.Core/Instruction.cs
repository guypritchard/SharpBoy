using System;
namespace GB.Emulator.Core
{
  /// <summary>Instruction description and handler. The owning CPU advances PC.</summary>
  public sealed class Instruction
  {
    public Instruction(byte value, string name, Action<byte, byte> action, ushort length, bool incrementPc = true)
    { Value = value; Name = name; Action = action; Length = length; IncrementPc = incrementPc; }
    public bool IncrementPc { get; }
    public short Value { get; }
    public string Name { get; }
    private Action<byte, byte> Action { get; }
    public ushort Length { get; }
    public override string ToString() => Name;
    internal void Execute(byte p1, byte p2) => Action(p1, p2);

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
