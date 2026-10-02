using System.Threading.Tasks;

namespace GB.Emulator;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var terminal = ConsoleTerminal.Open();
        if (terminal is null) return;

        using var emulator = await ConsoleEmulator.LoadAsync(args);
        emulator.Run(terminal.Stopping);
    }
}
