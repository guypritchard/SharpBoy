namespace GB.Experiments.NetworkCartridge;

/// <summary>IPv4 address in network numerical order; UDP port as a host-order integer.</summary>
public readonly record struct DatagramEndpoint(uint Address, ushort Port);
public sealed record NetworkDatagram(DatagramEndpoint Source, DatagramEndpoint Destination, ReadOnlyMemory<byte> Payload);

/// <summary>Preserves UDP message boundaries and endpoints; implementations must honor cancellation.</summary>
public interface IDatagramCartridgeBackend : IAsyncDisposable
{
    ValueTask OpenAsync(ushort localPort, CancellationToken cancellationToken);
    ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
    ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken cancellationToken);
    ValueTask CloseAsync(CancellationToken cancellationToken);
}
