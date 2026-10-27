using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace GB.Experiments.NetworkCartridge;

public sealed record LeaseTelemetry(byte NodeId, LocalNetworkTransport Transport, uint Address, uint Server,
    uint Mask, uint Router, uint Dns, uint LeaseSeconds)
{
    public static LeaseTelemetry Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != 32 || !packet[..4].SequenceEqual("GBT1"u8) || packet[5] > 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(packet[6..]) != 24) throw new InvalidDataException("Invalid telemetry record.");
        return new(packet[4], (LocalNetworkTransport)packet[5],
            BinaryPrimitives.ReadUInt32BigEndian(packet[8..]), BinaryPrimitives.ReadUInt32BigEndian(packet[12..]),
            BinaryPrimitives.ReadUInt32BigEndian(packet[16..]), BinaryPrimitives.ReadUInt32BigEndian(packet[20..]),
            BinaryPrimitives.ReadUInt32BigEndian(packet[24..]), BinaryPrimitives.ReadUInt32BigEndian(packet[28..]));
    }
}

public static class NetworkTelemetryExperiment
{
    public static async Task<LeaseTelemetry> RunAsync(LocalNetworkTransport transport, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var listener = new LocalRecordListener(transport);
        Task<NativeNodeResult> node = NativeNetworkNode.RunAsync("Telemetry", 1, transport, listener.Port, timeout.Token);
        try
        {
            byte[] record = await listener.ReceiveAsync(32, timeout.Token);
            LeaseTelemetry telemetry = LeaseTelemetry.Decode(record);
            NativeNodeResult result = await node;
            if (result.Result != 1 || telemetry.NodeId != 1 || telemetry.Transport != transport ||
                !record.AsSpan().SequenceEqual(result.Mailbox.AsSpan(0x70, 32)))
                throw new IOException("The guest did not successfully emit its own lease values.");
            return telemetry;
        }
        finally { timeout.Cancel(); try { await node; } catch (OperationCanceledException) when (timeout.IsCancellationRequested) { } }
    }
}

/// <summary>One loopback peer. TCP uses exact-length records; UDP verifies the peer and datagram size.</summary>
internal sealed class LocalRecordListener : IDisposable
{
    private readonly UdpClient? udp;
    private readonly TcpListener? tcp;
    private TcpClient? client;
    private IPEndPoint? peer;
    public ushort Port { get; }
    public LocalRecordListener(LocalNetworkTransport transport, ushort port = 0)
    {
        if (transport == LocalNetworkTransport.Udp)
        { this.udp = new(new IPEndPoint(IPAddress.Loopback, port)); Port = (ushort)((IPEndPoint)this.udp.Client.LocalEndPoint!).Port; }
        else if (transport == LocalNetworkTransport.Tcp)
        { this.tcp = new(IPAddress.Loopback, port); this.tcp.Start(); Port = (ushort)((IPEndPoint)this.tcp.LocalEndpoint).Port; }
        else throw new ArgumentOutOfRangeException(nameof(transport));
    }
    public async Task<byte[]> ReceiveAsync(int length, CancellationToken token)
    {
        if (this.udp != null)
        {
            while (true)
            {
                UdpReceiveResult packet = await this.udp.ReceiveAsync(token);
                if (this.peer != null && !packet.RemoteEndPoint.Equals(this.peer)) continue;
                if (packet.Buffer.Length != length) throw new InvalidDataException("Incorrect UDP record length.");
                this.peer ??= packet.RemoteEndPoint;
                return packet.Buffer;
            }
        }
        this.client ??= await this.tcp!.AcceptTcpClientAsync(token);
        byte[] bytes = new byte[length];
        await this.client.GetStream().ReadExactlyAsync(bytes, token);
        return bytes;
    }
    public async Task SendAsync(byte[] bytes, int fragmentSize, CancellationToken token)
    {
        if (this.udp != null) { await this.udp.SendAsync(bytes, this.peer ?? throw new IOException("No UDP worker registered."), token); return; }
        var stream = this.client?.GetStream() ?? throw new IOException("No TCP worker connected.");
        for (int offset = 0; offset < bytes.Length; offset += fragmentSize)
        {
            await stream.WriteAsync(bytes.AsMemory(offset, Math.Min(fragmentSize, bytes.Length - offset)), token);
            if (fragmentSize < bytes.Length) await Task.Delay(2, token);
        }
    }
    public void Dispose() { this.client?.Dispose(); this.tcp?.Stop(); this.udp?.Dispose(); }
}
