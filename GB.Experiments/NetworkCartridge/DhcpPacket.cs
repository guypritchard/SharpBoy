using System.Buffers.Binary;

namespace GB.Experiments.NetworkCartridge;

public enum DhcpMessageType : byte { Discover = 1, Offer, Request, Decline, Ack, Nak, Release }
public sealed record DhcpMessage(byte Operation, uint Transaction, byte[] HardwareAddress, uint OfferedAddress,
    DhcpMessageType Type, IReadOnlyDictionary<byte, byte[]> Options);

/// <summary>BOOTP/DHCP payload codec for the virtual server and ROM packet templates (RFC 2131/2132).</summary>
public static class DhcpPacket
{
    public static ReadOnlySpan<byte> ClientHardwareAddress => [0x02, 0x53, 0x42, 0x00, 0x00, 0x01];
    public const uint InitialTransaction = 0x534D3833;
    public const uint ServerAddress = 0xC0000201, ClientAddress = 0xC000022A;
    public const uint SubnetMask = 0xFFFFFF00, Gateway = ServerAddress, Dns = 0xC0000235, LeaseSeconds = 3600;

    public static byte[] Word(uint value)
    { var result = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(result, value); return result; }

    public static byte[] CreateClient(DhcpMessageType type, uint transaction = InitialTransaction,
        uint? requestedAddress = null, uint? server = null)
    {
        var options = new List<(byte, byte[])> { (53, [(byte)type]), (61, [1, .. ClientHardwareAddress]),
            (55, [1, 3, 6, 51]), (57, [4, 0]) }; // maximum DHCP payload: 1024 bytes
        if (requestedAddress.HasValue) options.Add((50, Word(requestedAddress.Value)));
        if (server.HasValue) options.Add((54, Word(server.Value)));
        return Create(1, transaction, ClientHardwareAddress, 0, options);
    }

    public static byte[] Create(byte operation, uint transaction, ReadOnlySpan<byte> hardwareAddress, uint offeredAddress,
        IEnumerable<(byte Tag, byte[] Value)> options)
    {
        if (hardwareAddress.Length != 6) throw new ArgumentException("Ethernet hardware address must be six bytes.", nameof(hardwareAddress));
        var packet = new List<byte>(new byte[240]);
        packet[0] = operation; packet[1] = 1; packet[2] = 6;
        packet[10] = 0x80; // BROADCAST before the virtual client owns an address
        byte[] xid = Word(transaction), yiaddr = Word(offeredAddress);
        for (int i = 0; i < 4; i++) { packet[4 + i] = xid[i]; packet[16 + i] = yiaddr[i]; }
        for (int i = 0; i < 6; i++) packet[28 + i] = hardwareAddress[i];
        packet[236] = 99; packet[237] = 130; packet[238] = 83; packet[239] = 99;
        foreach (var (tag, value) in options)
        {
            if (tag is 0 or 255 || value.Length > 255) throw new ArgumentException("Invalid DHCP option.", nameof(options));
            packet.Add(tag); packet.Add((byte)value.Length); packet.AddRange(value);
        }
        packet.Add(255);
        while (packet.Count < 300) packet.Add(0);
        if (packet.Count > NetworkCartridgeDevice.DatagramCapacity) throw new ArgumentException("Packet is too large.", nameof(options));
        return packet.ToArray();
    }

    public static bool TryRead(ReadOnlySpan<byte> packet, out DhcpMessage? message)
    {
        message = null;
        if (packet.Length is < 240 or > NetworkCartridgeDevice.DatagramCapacity || packet[0] is not (1 or 2) ||
            packet[1] != 1 || packet[2] != 6 || !packet.Slice(236, 4).SequenceEqual(new byte[] {99, 130, 83, 99})) return false;
        var options = new Dictionary<byte, byte[]>();
        bool ended = false;
        for (int i = 240; i < packet.Length;)
        {
            byte tag = packet[i++];
            if (tag == 0) continue;
            if (tag == 255) { ended = true; break; }
            if (i == packet.Length) return false;
            int length = packet[i++];
            if (length > packet.Length - i || options.ContainsKey(tag)) return false;
            options.Add(tag, packet.Slice(i, length).ToArray()); i += length;
        }
        if (!ended || !options.TryGetValue(53, out byte[]? type) || type.Length != 1 || type[0] is < 1 or > 7) return false;
        message = new(packet[0], BinaryPrimitives.ReadUInt32BigEndian(packet[4..]), packet.Slice(28, 6).ToArray(),
            BinaryPrimitives.ReadUInt32BigEndian(packet[16..]), (DhcpMessageType)type[0], options);
        return true;
    }
}
