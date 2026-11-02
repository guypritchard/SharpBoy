using System.Text;
using GB.Emulator.Core;

namespace GB.Experiments;

/// <summary>Runs a link-port protocol on an emulated Game Boy.</summary>
public sealed class GameboySerialServer : ISerialRequestHandler
{
    private const int MaxInstructionsPerByte = 12_000_000;
    private const int MaxResponseBytes = 4_096;
    private readonly Gameboy gameboy = new();

    public GameboySerialServer(byte[] rom)
    {
        ArgumentNullException.ThrowIfNull(rom);
        this.gameboy.Load(new Cartridge { Data = rom });
    }

    public string GetPage()
    {
        this.WaitForExternalTransfer();
        this.gameboy.Serial.ClockExternal((byte)'G');
        return Encoding.ASCII.GetString(this.ReadResponse());
    }

    public Task<string> GetPageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.GetPage());
    }

    public int GetPrime(int index)
    {
        if (index is < 1 or > DemoRom.MaximumPrimeIndex)
            throw new ArgumentOutOfRangeException(nameof(index));

        this.WaitForExternalTransfer();
        this.gameboy.Serial.ClockExternal((byte)'P');
        this.WaitForExternalTransfer();
        this.gameboy.Serial.ClockExternal((byte)index);
        this.WaitForExternalTransfer();
        this.gameboy.Serial.ClockExternal((byte)(index >> 8));

        // A generous search ceiling keeps small requests quick. For indices
        // 1..6542, the nth prime is below min(65535, 16*n).
        int ceiling = Math.Min(ushort.MaxValue, index * 16);
        this.WaitForExternalTransfer();
        this.gameboy.Serial.ClockExternal((byte)ceiling);
        this.WaitForExternalTransfer();
        this.gameboy.Serial.ClockExternal((byte)(ceiling >> 8));

        this.WaitForExternalTransfer();
        byte low = this.gameboy.Serial.ClockExternal(0xFF);
        this.WaitForExternalTransfer();
        byte high = this.gameboy.Serial.ClockExternal(0xFF);
        return low | (high << 8);
    }

    public Task<int> GetPrimeAsync(int index, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(this.GetPrime(index));
    }

    private byte[] ReadResponse()
    {
        var response = new List<byte>();
        for (int i = 0; i < MaxResponseBytes; i++)
        {
            this.WaitForExternalTransfer();
            byte outgoing = this.gameboy.Serial.ClockExternal(0xFF);
            if (outgoing == 0) return response.ToArray();
            response.Add(outgoing);
        }

        throw new InvalidOperationException("The Game Boy response exceeded the serial protocol limit.");
    }

    private void WaitForExternalTransfer()
    {
        for (int i = 0; i < MaxInstructionsPerByte; i++)
        {
            if (this.gameboy.Serial.IsTransferPending && this.gameboy.Serial.UsesExternalClock)
                return;
            this.gameboy.Step();
        }

        throw new TimeoutException("The Game Boy did not arm an externally clocked serial transfer.");
    }
}
