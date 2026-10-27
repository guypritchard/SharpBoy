using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class DhcpScreenAndEthernetTests
{
    [TestMethod]
    public async Task NativeScreenShowsProgressAndDecimalAddressesUsingRealTilePixels()
    {
        var lease = new VirtualDhcpLease(0x01020304, 0x01020301, 0xFFFFFF00, 0x01020301, 0x01020335, 3600);
        await using var session = new DhcpSession(new VirtualDhcpNetwork(new() { Lease = lease }));
        var statuses = new HashSet<string>();
        string Row(int row) => Encoding.ASCII.GetString(session.Host.Inspection.ReadMemory((ushort)(0x9801 + row * 32), 18).Bytes).TrimEnd();
        session.Host.FrameReady += _ => statuses.Add(Row(4));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!session.IsComplete) { deadline.Token.ThrowIfCancellationRequested(); session.Advance(TimeSpan.FromSeconds(1.0 / 60)); await Task.Yield(); }
        for (int i = 0; i < 3; i++) session.Advance(TimeSpan.FromSeconds(1.0 / 60));
        Assert.Contains("DISCOVER", statuses); Assert.Contains("REQUESTING", statuses);
        Assert.Contains("OFFER RECEIVED", statuses); Assert.Contains("ACK RECEIVED", statuses);
        Assert.AreEqual("IP ACQUIRED", Row(4)); Assert.AreEqual("1.2.3.4", Row(7));
        Assert.AreEqual("1.2.3.1", Row(10)); Assert.AreEqual("1.2.3.53", Row(13));
        Assert.IsGreaterThan(500, session.Host.LatestFrame!.Pixels.ToArray().Count(p => p == 3));
    }

    [TestMethod]
    public async Task FailureIsVisibleOnScreenWithoutAnAcquiredAddress()
    {
        await using var session = new DhcpSession(new VirtualDhcpNetwork(new() { Silent = true }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!session.IsComplete) { deadline.Token.ThrowIfCancellationRequested(); session.Advance(TimeSpan.FromSeconds(1.0 / 60)); }
        Assert.AreEqual("FAILED - NO LEASE", Encoding.ASCII.GetString(session.Host.Inspection.ReadMemory(0x9881, 18).Bytes).TrimEnd());
        Assert.IsFalse(session.CaptureResult().Acquired);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(300)] [DataRow(1024)]
    public void EthernetUdpRoundTripPreservesPacketBoundariesChecksumsAndPadding(int length)
    {
        byte[] payload = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var packet = new NetworkDatagram(new(0, 68), new(uint.MaxValue, 67), payload);
        byte[] frame = EthernetUdp.Encode(DhcpPacket.ClientHardwareAddress, Enumerable.Repeat((byte)255, 6).ToArray(), packet);
        Assert.IsGreaterThanOrEqualTo(60, frame.Length);
        Assert.IsTrue(EthernetUdp.TryDecode(frame, DhcpPacket.ClientHardwareAddress, out NetworkDatagram decoded));
        Assert.AreEqual(packet.Source, decoded.Source); Assert.AreEqual(packet.Destination, decoded.Destination);
        CollectionAssert.AreEqual(payload, decoded.Payload.ToArray());
        frame[24] ^= 1; // IPv4 checksum corruption
        Assert.IsFalse(EthernetUdp.TryDecode(frame, DhcpPacket.ClientHardwareAddress, out _));
        frame[24] ^= 1; frame[40] ^= 1; // UDP checksum corruption, including empty datagrams
        Assert.IsFalse(EthernetUdp.TryDecode(frame, DhcpPacket.ClientHardwareAddress, out _));
    }

    [TestMethod]
    public void VlanEthernetAndIpv4ZeroUdpChecksumAreSupported()
    {
        byte[] frame = EthernetUdp.Encode(DhcpPacket.ClientHardwareAddress, DhcpPacket.ClientHardwareAddress,
            new(new(DhcpPacket.ServerAddress, 67), new(uint.MaxValue, 68), new byte[300]));
        frame[40] = 0; frame[41] = 0; // permitted by IPv4 UDP
        var tagged = new byte[frame.Length + 4];
        frame.AsSpan(0, 12).CopyTo(tagged); tagged[12] = 0x81; tagged[13] = 0;
        frame.AsSpan(12).CopyTo(tagged.AsSpan(16));
        Assert.IsTrue(EthernetUdp.TryDecode(tagged, DhcpPacket.ClientHardwareAddress, out NetworkDatagram decoded));
        Assert.AreEqual((ushort)68, decoded.Destination.Port);
    }

    [TestMethod]
    public async Task NativeDhcpWorksThroughActualEthernetIpv4UdpFrames()
    {
        var link = new TestEthernetLink();
        var result = await DhcpExperiment.RunRomAsync(new EthernetDhcpBackend(link));
        Assert.IsTrue(result.Acquired); Assert.AreEqual(DhcpPacket.ClientAddress, result.Address);
        Assert.AreEqual(2, link.SentCount); Assert.IsTrue(link.Disposed);
    }

    [TestMethod]
    public void TruncatedFragmentedAndForeignEthernetFramesAreRejected()
    {
        byte[] frame = EthernetUdp.Encode(DhcpPacket.ClientHardwareAddress, DhcpPacket.ClientHardwareAddress,
            new(new(DhcpPacket.ServerAddress, 67), new(uint.MaxValue, 68), new byte[300]));
        for (int length = 0; length < 42; length++) Assert.IsFalse(EthernetUdp.TryDecode(frame.AsSpan(0, length), DhcpPacket.ClientHardwareAddress, out _));
        Assert.IsFalse(EthernetUdp.TryDecode(frame.AsSpan(0, frame.Length - 1), DhcpPacket.ClientHardwareAddress, out _));
        frame[0] ^= 2; Assert.IsFalse(EthernetUdp.TryDecode(frame, DhcpPacket.ClientHardwareAddress, out _));
        frame[0] ^= 2; frame[20] = 0x20; Assert.IsFalse(EthernetUdp.TryDecode(frame, DhcpPacket.ClientHardwareAddress, out _));
    }

    private sealed class TestEthernetLink : IEthernetLink
    {
        private readonly VirtualDhcpNetwork server = new();
        public int SentCount;
        public bool Disposed;
        public TestEthernetLink() => this.server.OpenAsync(68, CancellationToken.None).GetAwaiter().GetResult();
        public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            Assert.IsTrue(EthernetUdp.TryDecode(frame.Span, DhcpPacket.ClientHardwareAddress, out NetworkDatagram request));
            Assert.AreEqual(new DatagramEndpoint(0, 68), request.Source);
            Assert.AreEqual(new DatagramEndpoint(uint.MaxValue, 67), request.Destination);
            this.SentCount++; await this.server.SendAsync(request.Destination, request.Payload, token);
        }
        public async ValueTask<byte[]> ReceiveAsync(CancellationToken token)
        {
            NetworkDatagram reply = await this.server.ReceiveAsync(token);
            return EthernetUdp.Encode(new byte[] {2, 0, 0, 0, 0, 254}, DhcpPacket.ClientHardwareAddress, reply);
        }
        public ValueTask DisposeAsync() { this.Disposed = true; return this.server.DisposeAsync(); }
    }
}
