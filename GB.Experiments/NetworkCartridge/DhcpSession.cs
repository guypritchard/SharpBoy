using System.Buffers.Binary;
using GB.Emulator.Core;
using GB.Emulator.Hosting;

namespace GB.Experiments.NetworkCartridge;

/// <summary>Owns the experimental machine, device and shared playback host on one execution context.</summary>
public sealed class DhcpSession : IAsyncDisposable
{
    private readonly Gameboy machine = new();
    private readonly NetworkCartridgeDevice device;
    private bool disposed;
    public EmulationHost Host { get; }
    // The guest publishes its result only after finishing all screen writes.
    public bool IsComplete => this.machine.Memory.Peek(DhcpCartridgeRom.Result) is 1 or 255;
    public bool HasPendingOperation => this.device.HasPendingOperation;

    public DhcpSession(IDatagramCartridgeBackend backend)
    {
        this.device = new(backend);
        this.Host = new(this.machine, options: new() { HistoryCapacity = 0 });
        byte[] rom = DhcpCartridgeRom.Create();
        this.Host.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, Header.Length).ToArray()) });
        this.machine.Memory.AddDevice(this.device, takePriority: true);
        this.Host.Resume();
    }
    public void Advance(TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        this.device.Pump(); this.Host.Advance(elapsed);
    }
    public DhcpExperimentResult CaptureResult()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        uint Read(ushort address)
        {
            Span<byte> bytes = stackalloc byte[4];
            for (int i = 0; i < 4; i++) bytes[i] = this.machine.Memory.Peek((ushort)(address + i));
            return BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }
        bool acquired = this.IsComplete && this.machine.Memory.Peek(DhcpCartridgeRom.Result) == 1;
        return new(acquired, this.machine.Memory.Peek(DhcpCartridgeRom.State),
            acquired ? Read(DhcpCartridgeRom.Address) : 0, acquired ? Read(DhcpCartridgeRom.Server) : 0,
            acquired ? Read(DhcpCartridgeRom.Mask) : 0, acquired ? Read(DhcpCartridgeRom.Router) : 0,
            acquired ? Read(DhcpCartridgeRom.Dns) : 0, acquired ? Read(DhcpCartridgeRom.Lease) : 0, this.machine.EmulatedCycles);
    }
    public async ValueTask DisposeAsync()
    {
        if (this.disposed) return;
        this.disposed = true; this.Host.Dispose(); await this.device.DisposeAsync();
    }
}

/// <summary>Makes each receive visible at real-time playback speed without changing the guest protocol.</summary>
public sealed class DelayedDatagramBackend(IDatagramCartridgeBackend inner, TimeSpan delay) : IDatagramCartridgeBackend
{
    public ValueTask OpenAsync(ushort port, CancellationToken token) => inner.OpenAsync(port, token);
    public ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken token) => inner.SendAsync(destination, payload, token);
    public async ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken token)
    { await Task.Delay(delay, token); return await inner.ReceiveAsync(token); }
    public ValueTask CloseAsync(CancellationToken token) => inner.CloseAsync(token);
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
