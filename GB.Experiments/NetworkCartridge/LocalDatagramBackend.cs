using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace GB.Experiments.NetworkCartridge;

/// <summary>Real loopback UDP, preserving datagrams (including empty payloads). No LAN identity is implied.</summary>
public sealed class LocalDatagramBackend : IDatagramCartridgeBackend
{
    private UdpClient? socket;
    private bool disposed;
    public ValueTask OpenAsync(ushort localPort, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(this.disposed, this);
        this.socket?.Dispose(); this.socket = new(new IPEndPoint(IPAddress.Loopback, localPort));
        return ValueTask.CompletedTask;
    }
    public async ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        if ((destination.Address >> 24) != 127 || destination.Port == 0)
            throw new IOException("The local experiment accepts only loopback UDP destinations with nonzero ports.");
        await this.Socket.SendAsync(payload, new IPEndPoint(new IPAddress(DhcpPacket.Word(destination.Address)), destination.Port), token);
    }
    public async ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken token)
    {
        UdpReceiveResult received = await this.Socket.ReceiveAsync(token);
        var local = (IPEndPoint)this.Socket.Client.LocalEndPoint!;
        return new(new(BinaryPrimitives.ReadUInt32BigEndian(received.RemoteEndPoint.Address.GetAddressBytes()), (ushort)received.RemoteEndPoint.Port),
            new(0x7F000001, (ushort)local.Port), received.Buffer);
    }
    private UdpClient Socket => this.socket ?? throw new IOException("UDP port is closed.");
    public ValueTask CloseAsync(CancellationToken token)
    { token.ThrowIfCancellationRequested(); this.socket?.Dispose(); this.socket = null; return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync()
    { this.disposed = true; this.socket?.Dispose(); this.socket = null; return ValueTask.CompletedTask; }
}

/// <summary>DHCP stays on a simulated segment; application traffic uses real localhost UDP.</summary>
public sealed class LocalDhcpBackend(VirtualDhcpOptions? options = null) : IDatagramCartridgeBackend
{
    private readonly VirtualDhcpNetwork dhcp = new(options);
    private readonly LocalDatagramBackend application = new();
    private IDatagramCartridgeBackend? active;
    public async ValueTask OpenAsync(ushort port, CancellationToken token)
    {
        if (this.active != null) await this.active.CloseAsync(token);
        this.active = port == 68 ? this.dhcp : this.application;
        await this.active.OpenAsync(port, token);
    }
    public ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken token) =>
        this.Active.SendAsync(destination, payload, token);
    public ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken token) => this.Active.ReceiveAsync(token);
    private IDatagramCartridgeBackend Active => this.active ?? throw new IOException("UDP port is closed.");
    public async ValueTask CloseAsync(CancellationToken token)
    { if (this.active != null) await this.active.CloseAsync(token); this.active = null; }
    public async ValueTask DisposeAsync()
    { try { await this.dhcp.DisposeAsync(); } finally { await this.application.DisposeAsync(); } }
}
