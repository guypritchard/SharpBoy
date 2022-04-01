namespace GB.Emulator.Core
{
  public partial class Cpu
  {
    private static Memory memory;
    public static Memory Memory {
      get {
        return Cpu.memory;
      }
    }
  }
}