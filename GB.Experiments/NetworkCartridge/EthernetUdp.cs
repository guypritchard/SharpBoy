using System.Buffers.Binary;

namespace GB.Experiments.NetworkCartridge;

public interface IEthernetLink : IAsyncDisposable
{
    ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
    ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>Ethernet II / IPv4 / UDP framing. Does not implement fragmentation or routing.</summary>
public static class EthernetUdp
{
    public static byte[] Encode(ReadOnlySpan<byte> sourceMac, ReadOnlySpan<byte> destinationMac, NetworkDatagram packet, ushort identifier = 0)
    {
        if (sourceMac.Length != 6 || destinationMac.Length != 6) throw new ArgumentException("Ethernet addresses must be six bytes.");
        if (packet.Payload.Length > 1472) throw new ArgumentException("UDP payload exceeds the supported Ethernet MTU.");
        int ipLength = 28 + packet.Payload.Length;
        var frame = new byte[Math.Max(60, 14 + ipLength)];
        destinationMac.CopyTo(frame); sourceMac.CopyTo(frame.AsSpan(6)); Write16(frame, 12, 0x0800);
        Span<byte> ip = frame.AsSpan(14, ipLength);
        ip[0] = 0x45; Write16(ip, 2, (ushort)ipLength); Write16(ip, 4, identifier); Write16(ip, 6, 0x4000);
        ip[8] = 64; ip[9] = 17;
        BinaryPrimitives.WriteUInt32BigEndian(ip[12..], packet.Source.Address);
        BinaryPrimitives.WriteUInt32BigEndian(ip[16..], packet.Destination.Address);
        Write16(ip, 10, Checksum(ip[..20]));
        Span<byte> udp = ip[20..];
        Write16(udp, 0, packet.Source.Port); Write16(udp, 2, packet.Destination.Port); Write16(udp, 4, (ushort)udp.Length);
        packet.Payload.Span.CopyTo(udp[8..]);
        ushort checksum = Checksum(udp, PseudoHeaderSum(ip, udp.Length));
        Write16(udp, 6, checksum == 0 ? ushort.MaxValue : checksum);
        return frame;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, ReadOnlySpan<byte> localMac, out NetworkDatagram? packet)
    {
        packet = null;
        if (localMac.Length != 6 || frame.Length < 42) return false;
        if (!frame[..6].SequenceEqual(localMac) && !frame[..6].SequenceEqual(new byte[] {255,255,255,255,255,255})) return false;
        int offset = 14; ushort type = Read16(frame, 12);
        for (int tags = 0; type is 0x8100 or 0x88A8 && tags < 2; tags++)
        {
            if (frame.Length < offset + 4) return false;
            type = Read16(frame, offset + 2); offset += 4;
        }
        if (type != 0x0800 || frame.Length < offset + 28) return false;
        ReadOnlySpan<byte> ip = frame[offset..];
        int headerLength = (ip[0] & 15) * 4, total = Read16(ip, 2);
        if (ip[0] >> 4 != 4 || headerLength < 20 || total < headerLength + 8 || total > ip.Length || ip[9] != 17 ||
            (Read16(ip, 6) & 0xBFFF) != 0 || Checksum(ip[..headerLength]) != 0) return false;
        ReadOnlySpan<byte> udp = ip.Slice(headerLength, total - headerLength);
        if (Read16(udp, 4) != udp.Length || (Read16(udp, 6) != 0 && Checksum(udp, PseudoHeaderSum(ip, udp.Length)) != 0)) return false;
        packet = new(new(BinaryPrimitives.ReadUInt32BigEndian(ip[12..]), Read16(udp, 0)),
            new(BinaryPrimitives.ReadUInt32BigEndian(ip[16..]), Read16(udp, 2)), udp[8..].ToArray());
        return true;
    }
    private static uint PseudoHeaderSum(ReadOnlySpan<byte> ip, int length) =>
        (uint)(Read16(ip, 12) + Read16(ip, 14) + Read16(ip, 16) + Read16(ip, 18) + 17 + length);
    private static ushort Checksum(ReadOnlySpan<byte> bytes, uint sum = 0)
    {
        for (int i = 0; i < bytes.Length; i += 2) sum += (uint)(bytes[i] << 8 | (i + 1 < bytes.Length ? bytes[i + 1] : 0));
        while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        return (ushort)~sum;
    }
    private static ushort Read16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
    private static void Write16(Span<byte> bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes[offset..], value);
}

/// <summary>DHCP-only Ethernet path: broadcasts from 0.0.0.0:68, receives server replies to the guest MAC.</summary>
public sealed class EthernetDhcpBackend(IEthernetLink link) : IDatagramCartridgeBackend
{
    private bool opened;
    private ushort identifier;
    public ValueTask OpenAsync(ushort port, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (port != 68) throw new InvalidOperationException("This Ethernet backend implements DHCP acquisition on UDP port 68.");
        this.opened = true; return ValueTask.CompletedTask;
    }
    public ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!this.opened || destination != new DatagramEndpoint(uint.MaxValue, 67))
            throw new InvalidOperationException("DHCP must broadcast to UDP port 67.");
        byte[] frame = EthernetUdp.Encode(DhcpPacket.ClientHardwareAddress, new byte[] {255,255,255,255,255,255},
            new(new(0, 68), destination, payload), this.identifier++);
        return link.SendAsync(frame, token);
    }
    public async ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken token)
    {
        if (!this.opened) throw new IOException("Ethernet DHCP port is closed.");
        int inspected = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            byte[] frame = await link.ReceiveAsync(token);
            if (EthernetUdp.TryDecode(frame, DhcpPacket.ClientHardwareAddress, out NetworkDatagram? packet) &&
                packet!.Source.Port == 67 && packet.Destination.Port == 68 && packet.Payload.Length <= NetworkCartridgeDevice.DatagramCapacity)
                return packet;
            if (++inspected % 32 == 0) await Task.Yield();
        }
    }
    public ValueTask CloseAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); this.opened = false; return ValueTask.CompletedTask; }
    public ValueTask DisposeAsync() { this.opened = false; return link.DisposeAsync(); }
}
