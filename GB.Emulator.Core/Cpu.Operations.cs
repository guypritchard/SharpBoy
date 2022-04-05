namespace GB.Emulator.Core
{
  public partial class Cpu
  {

    public static class Operations
    {
      public static byte Decrement(byte value)
      {
        byte local = value;

        // This is a subtract operation so set the 'subtract' flag.
        Cpu.Flags.N = true;

        // Set the half carry flag if we're going to 'carry the one' from the ones to the tens unit in hex.
        Cpu.Flags.H = (local & 0x0F) == 0;

        local -= 1;

        // Set the 'zero' flag if the value is indeed now zero.
        Cpu.Flags.Z = local == 0;

        return local;
      }
      public static byte Increment(byte value)
      {
        byte local = value;

        // This is not a subtract operation so set the 'subtract' flag to false.
        Cpu.Flags.N = false;

        local += 1;

        // Set the 'zero' flag if the value is indeed now zero.
        Cpu.Flags.Z = local == 0;

        return local;
      }
    }
  }
}