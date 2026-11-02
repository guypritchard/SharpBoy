using System.Net;
using System.Net.Sockets;

namespace GB.Experiments.NetworkCartridge;

public interface INetworkCartridgeBackend : IAsyncDisposable
{
    ValueTask ConnectAsync(CancellationToken cancellationToken);
    ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask CloseAsync(CancellationToken cancellationToken);
}

/// <summary>Deterministic bounded echo transport with intentionally fragmented receives.</summary>
public sealed class LoopbackNetworkBackend(int fragmentSize = 7) : INetworkCartridgeBackend
{
    private readonly int receiveFragmentSize = fragmentSize is >= 1 and <= NetworkCartridgeDevice.Capacity
        ? fragmentSize : throw new ArgumentOutOfRangeException(nameof(fragmentSize));
    private readonly Queue<byte> bytes = new();
    private bool connected;
    public ValueTask ConnectAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); this.connected = true; this.bytes.Clear(); return ValueTask.CompletedTask; }
    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!this.connected) throw new IOException("Disconnected.");
        if (data.Length + this.bytes.Count > NetworkCartridgeDevice.Capacity) throw new IOException("Echo buffer is full.");
        foreach (byte value in data.Span) this.bytes.Enqueue(value);
        return ValueTask.CompletedTask;
    }
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!this.connected) throw new IOException("Disconnected.");
        int count = Math.Min(Math.Min(buffer.Length, this.receiveFragmentSize), this.bytes.Count);
        for (int i = 0; i < count; i++) buffer.Span[i] = this.bytes.Dequeue();
        if (count == 0) this.connected = false; // deterministic EOF after the echoed data
        return ValueTask.FromResult(count);
    }
    public ValueTask CloseAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); this.connected = false; this.bytes.Clear(); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { this.connected = false; this.bytes.Clear(); return ValueTask.CompletedTask; }
}

/// <summary>One host-configured loopback TCP destination; ROM bytes cannot choose arbitrary endpoints.</summary>
public sealed class TcpNetworkBackend(int port) : INetworkCartridgeBackend
{
    private readonly int destinationPort = port is >= 1 and <= 65535 ? port : throw new ArgumentOutOfRangeException(nameof(port));
    private TcpClient? client;
    private string? localEndpoint;
    private long sentBytes, receivedBytes;
    public TcpTransportSnapshot CaptureStatistics() => new(localEndpoint, $"127.0.0.1:{destinationPort}",
        Interlocked.Read(ref sentBytes), Interlocked.Read(ref receivedBytes));
    public async ValueTask ConnectAsync(CancellationToken cancellationToken)
    {
        this.client?.Dispose();
        this.client = new TcpClient();
        await this.client.ConnectAsync(IPAddress.Loopback, this.destinationPort, cancellationToken);
        if (this.client.Client.LocalEndPoint is IPEndPoint endpoint)
            localEndpoint = $"{endpoint.Address.MapToIPv4()}:{endpoint.Port}";
    }
    public async ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        await this.Stream.WriteAsync(bytes, cancellationToken);
        Interlocked.Add(ref sentBytes, bytes.Length);
    }
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int count = await this.Stream.ReadAsync(buffer, cancellationToken);
        Interlocked.Add(ref receivedBytes, count); return count;
    }
    private NetworkStream Stream => this.client?.GetStream() ?? throw new IOException("Disconnected.");
    public ValueTask CloseAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); this.client?.Dispose(); this.client = null; return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { this.client?.Dispose(); this.client = null; return ValueTask.CompletedTask; }
}

public sealed record TcpTransportSnapshot(string? LocalEndpoint, string RemoteEndpoint, long SentBytes, long ReceivedBytes);
