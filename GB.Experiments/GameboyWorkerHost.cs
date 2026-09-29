using System.Globalization;
using System.Text;
using System.Diagnostics;

namespace GB.Experiments;

/// <summary>Runs one emulator behind a line-oriented pipe used by the parent process.</summary>
internal static class GameboyWorkerHost
{
    public static async Task RunAsync(string? romPath)
    {
        // Headless workers do not need per-instruction debugger traces.
        Trace.Listeners.Clear();
        byte[] rom = romPath is null ? DemoRom.Create() : File.ReadAllBytes(romPath);
        var server = new GameboySerialServer(rom);
        while (await Console.In.ReadLineAsync() is { } request)
        {
            string response;
            try
            {
                if (request == "G")
                {
                    response = "G " + Convert.ToBase64String(Encoding.UTF8.GetBytes(server.GetPage()));
                }
                else if (request.StartsWith("P ", StringComparison.Ordinal)
                    && int.TryParse(request.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    response = "P " + server.GetPrime(index).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    throw new InvalidDataException("Unknown worker request.");
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                response = "E " + Convert.ToBase64String(Encoding.UTF8.GetBytes(exception.Message));
            }

            await Console.Out.WriteLineAsync(response);
            await Console.Out.FlushAsync();
        }
    }
}
