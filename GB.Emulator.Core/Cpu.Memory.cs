namespace GB.Emulator.Core
{
  public partial class Cpu
  {
    private static MemoryMap memory;
    public static MemoryMap Memory {
      get {
        return Cpu.memory;
      }
    }
  }
}