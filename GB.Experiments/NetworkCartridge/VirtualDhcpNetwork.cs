using System.Buffers.Binary;

namespace GB.Experiments.NetworkCartridge;

public sealed record VirtualDhcpLease(uint Address, uint Server, uint Mask, uint Router, uint Dns, uint Seconds);

public sealed class VirtualDhcpOptions
{
    public VirtualDhcpLease Lease { get; init; } = new(DhcpPacket.ClientAddress, DhcpPacket.ServerAddress,
        DhcpPacket.SubnetMask, DhcpPacket.Gateway, DhcpPacket.Dns, DhcpPacket.LeaseSeconds);
    public bool DropFirstOffer { get; init; }
    public bool NakFirstRequest { get; init; }
    public bool DropFirstAck { get; init; }
    public bool Silent { get; init; }
    /// <summary>Test hook for loss, unrelated traffic and malformed server responses.</summary>
    public Func<NetworkDatagram, IEnumerable<NetworkDatagram>>? TransformReply { get; init; }
}

/// <summary>An isolated virtual UDP segment with one DHCP server. Never binds host UDP ports 67/68.</summary>
public sealed class VirtualDhcpNetwork(VirtualDhcpOptions? options = null) : IDatagramCartridgeBackend
{
    private readonly VirtualDhcpOptions options = options ?? new();
    private readonly Queue<NetworkDatagram> replies = new();
    private readonly List<DhcpMessageType> messages = new();
    private readonly List<NetworkDatagram> sent = new();
    private readonly List<string> transcript = new();
    private readonly HashSet<string> leases = new();
    private ushort port;
    private bool opened, disposed, offered, ackDropped;
    private uint offeredTransaction;
    private byte[] offeredClient = [];
    private int offers, requests;
    public IReadOnlyList<DhcpMessageType> Messages => this.messages.AsReadOnly();
    public IReadOnlyList<NetworkDatagram> Sent => this.sent.AsReadOnly();
    public IReadOnlyList<string> Transcript => this.transcript.AsReadOnly();
    public int Timeouts { get; private set; }
    public int LeasesGranted { get; private set; }

    public ValueTask OpenAsync(ushort localPort, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(this.disposed, this);
        this.port = localPort; this.opened = true; this.replies.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.opened) throw new IOException("Virtual UDP socket is closed.");
        if (this.sent.Count >= 64) throw new IOException("Virtual network request limit exceeded.");
        this.sent.Add(new(new(0, this.port), destination, payload.ToArray()));
        if (this.port != 68 || destination != new DatagramEndpoint(uint.MaxValue, 67) ||
            !DhcpPacket.TryRead(payload.Span, out DhcpMessage? message) || message!.Operation != 1) return ValueTask.CompletedTask;
        this.messages.Add(message.Type);
        this.transcript.Add($"client {message.Type}");
        if (this.options.Silent) return ValueTask.CompletedTask;
        DhcpMessageType response;
        if (message.Type == DhcpMessageType.Discover)
        {
            this.offered = true; this.offeredTransaction = message.Transaction; this.offeredClient = message.HardwareAddress;
            if (++this.offers == 1 && this.options.DropFirstOffer) return ValueTask.CompletedTask;
            response = DhcpMessageType.Offer;
        }
        else if (message.Type == DhcpMessageType.Request && this.offered && message.Transaction == this.offeredTransaction &&
            message.HardwareAddress.AsSpan().SequenceEqual(this.offeredClient) &&
            ReadOption(message, 50) == this.options.Lease.Address && ReadOption(message, 54) == this.options.Lease.Server)
        {
            response = ++this.requests == 1 && this.options.NakFirstRequest ? DhcpMessageType.Nak : DhcpMessageType.Ack;
            if (response == DhcpMessageType.Ack)
            {
                if (this.leases.Add($"{message.Transaction:X8}:{Convert.ToHexString(message.HardwareAddress)}")) this.LeasesGranted++;
                if (!this.ackDropped && this.options.DropFirstAck)
                { this.ackDropped = true; return ValueTask.CompletedTask; }
            }
            else this.offered = false;
        }
        else return ValueTask.CompletedTask;
        this.transcript.Add($"server {response}");
        var packet = DhcpPacket.Create(2, message.Transaction, message.HardwareAddress,
            response == DhcpMessageType.Nak ? 0 : this.options.Lease.Address,
            [(53, [(byte)response]), (54, DhcpPacket.Word(this.options.Lease.Server)),
             (51, DhcpPacket.Word(this.options.Lease.Seconds)), (1, DhcpPacket.Word(this.options.Lease.Mask)),
             (3, DhcpPacket.Word(this.options.Lease.Router)), (6, DhcpPacket.Word(this.options.Lease.Dns))]);
        var datagram = new NetworkDatagram(new(this.options.Lease.Server, 67), new(uint.MaxValue, 68), packet);
        foreach (NetworkDatagram reply in this.options.TransformReply?.Invoke(datagram) ?? [datagram])
        {
            if (this.replies.Count >= 8) throw new IOException("Virtual receive queue is full.");
            this.replies.Enqueue(reply with { Payload = reply.Payload.ToArray() });
        }
        return ValueTask.CompletedTask;
    }

    private static uint? ReadOption(DhcpMessage message, byte tag) =>
        message.Options.TryGetValue(tag, out byte[]? value) && value.Length == 4 ? BinaryPrimitives.ReadUInt32BigEndian(value) : null;

    public ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.opened) throw new IOException("Virtual UDP socket is closed.");
        while (this.replies.Count != 0)
        {
            NetworkDatagram packet = this.replies.Dequeue();
            if (packet.Destination.Port == this.port) return ValueTask.FromResult(packet);
        }
        this.Timeouts++;
        throw new TimeoutException("Virtual DHCP receive deadline elapsed.");
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); this.opened = false; this.replies.Clear(); return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { this.disposed = true; this.opened = false; this.replies.Clear(); return ValueTask.CompletedTask; }
}
