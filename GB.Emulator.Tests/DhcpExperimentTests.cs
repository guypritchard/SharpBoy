using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GB.Emulator.Core;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class DhcpExperimentTests
{
    [TestMethod]
    public async Task NativeRomAcquiresLeaseWithDiscoverOfferRequestAck()
    {
        var network = new VirtualDhcpNetwork();
        DhcpExperimentResult result = await DhcpExperiment.RunRomAsync(network);
        AssertLease(result);
        CollectionAssert.AreEqual(new[] { DhcpMessageType.Discover, DhcpMessageType.Request }, network.Messages.ToArray());
        Assert.AreEqual(1, network.LeasesGranted);
        Assert.AreEqual(0, network.Timeouts);
        Assert.AreEqual("192.0.2.42", DhcpExperiment.FormatAddress(result.Address));
        foreach (NetworkDatagram packet in network.Sent)
        {
            Assert.AreEqual(new DatagramEndpoint(0, 68), packet.Source);
            Assert.AreEqual(new DatagramEndpoint(uint.MaxValue, 67), packet.Destination);
            Assert.IsTrue(DhcpPacket.TryRead(packet.Payload.Span, out DhcpMessage message));
            Assert.AreEqual(DhcpPacket.InitialTransaction, message.Transaction);
            if (message.Type == DhcpMessageType.Request)
            {
                Assert.AreEqual(DhcpPacket.ClientAddress, BinaryPrimitives.ReadUInt32BigEndian(message.Options[50]));
                Assert.AreEqual(DhcpPacket.ServerAddress, BinaryPrimitives.ReadUInt32BigEndian(message.Options[54]));
            }
        }
    }

    [TestMethod]
    public async Task GuestUsesTheServersOfferedValuesInsteadOfHardcodedConfiguration()
    {
        var lease = new VirtualDhcpLease(0xCB00710C, 0xCB007101, 0xFFFFFF80, 0xCB007102, 0xCB007135, 0x01020304);
        var network = new VirtualDhcpNetwork(new() { Lease = lease });
        DhcpExperimentResult result = await DhcpExperiment.RunRomAsync(network);
        Assert.IsTrue(result.Acquired);
        Assert.AreEqual(lease.Address, result.Address); Assert.AreEqual(lease.Server, result.Server);
        Assert.AreEqual(lease.Mask, result.Mask); Assert.AreEqual(lease.Router, result.Router);
        Assert.AreEqual(lease.Dns, result.Dns); Assert.AreEqual(lease.Seconds, result.LeaseSeconds);
        Assert.IsTrue(DhcpPacket.TryRead(network.Sent[^1].Payload.Span, out DhcpMessage request));
        Assert.AreEqual(lease.Address, BinaryPrimitives.ReadUInt32BigEndian(request.Options[50]));
        Assert.AreEqual(lease.Server, BinaryPrimitives.ReadUInt32BigEndian(request.Options[54]));
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    public async Task LostPacketsAndNakAreRecoveredWithinBoundedRetries(bool dropOffer, bool dropAck, bool nak)
    {
        var network = new VirtualDhcpNetwork(new() { DropFirstOffer = dropOffer, DropFirstAck = dropAck, NakFirstRequest = nak });
        AssertLease(await DhcpExperiment.RunRomAsync(network));
        Assert.AreEqual(1, network.LeasesGranted);
        Assert.AreEqual((dropOffer ? 1 : 0) + (dropAck ? 1 : 0), network.Timeouts);
        Assert.IsLessThanOrEqualTo(8, network.Messages.Count);
        if (nak)
        {
            Assert.IsTrue(DhcpPacket.TryRead(network.Sent[0].Payload.Span, out DhcpMessage first));
            Assert.IsTrue(DhcpPacket.TryRead(network.Sent[^1].Payload.Span, out DhcpMessage last));
            Assert.AreNotEqual(first.Transaction, last.Transaction);
        }
    }

    [TestMethod]
    public async Task SilenceFailsAfterThreeDiscoverAttemptsWithoutPublishingAddress()
    {
        var network = new VirtualDhcpNetwork(new() { Silent = true });
        DhcpExperimentResult result = await DhcpExperiment.RunRomAsync(network);
        Assert.IsFalse(result.Acquired); Assert.AreEqual((byte)5, result.State);
        Assert.AreEqual(0u, result.Address); Assert.AreEqual(0u, result.LeaseSeconds);
        Assert.HasCount(3, network.Messages); Assert.AreEqual(3, network.Timeouts);
    }

    [TestMethod]
    public async Task UnrelatedRepliesAreIgnoredBeforeAcceptingTheMatchingTransaction()
    {
        var network = new VirtualDhcpNetwork(new() { TransformReply = packet =>
        {
            byte[] wrong = packet.Payload.ToArray(); wrong[4] ^= 1;
            return [packet with { Payload = wrong }, packet];
        }});
        AssertLease(await DhcpExperiment.RunRomAsync(network));
        Assert.AreEqual(0, network.Timeouts);
    }

    [TestMethod]
    public async Task GuestTlvParserHandlesPaddingUnknownOptionsAndSixteenBitLengths()
    {
        var network = new VirtualDhcpNetwork(new() { TransformReply = packet =>
        {
            Assert.IsTrue(DhcpPacket.TryRead(packet.Payload.Span, out DhcpMessage message));
            var options = message.Options.Reverse().Select(pair => (pair.Key, pair.Value)).ToList();
            options.Insert(0, (200, new byte[255])); options.Insert(1, (201, new byte[200]));
            byte[] bytes = DhcpPacket.Create(message.Operation, message.Transaction, message.HardwareAddress, message.OfferedAddress, options);
            var padded = bytes.Take(240).Concat(new byte[] { 0, 0, 0 }).Concat(bytes.Skip(240)).ToArray();
            Assert.IsGreaterThan(576, padded.Length);
            return [packet with { Payload = padded }];
        }});
        AssertLease(await DhcpExperiment.RunRomAsync(network));
    }

    [TestMethod]
    [DataRow("xid")]
    [DataRow("mac")]
    [DataRow("cookie")]
    [DataRow("opcode")]
    [DataRow("source-port")]
    [DataRow("source-address")]
    [DataRow("short-header")]
    [DataRow("truncated-option")]
    [DataRow("missing-end")]
    [DataRow("duplicate-type")]
    [DataRow("overload")]
    [DataRow("invalid-option-length")]
    public async Task GuestRejectsMalformedOrMismatchedReplies(string fault)
    {
        var network = new VirtualDhcpNetwork(new() { TransformReply = packet => [Corrupt(packet, fault)] });
        DhcpExperimentResult result = await DhcpExperiment.RunRomAsync(network);
        Assert.IsFalse(result.Acquired, fault); Assert.AreEqual(0u, result.Address);
        Assert.HasCount(3, network.Messages, fault);
    }

    [TestMethod]
    [DataRow("different-address")]
    [DataRow("different-server")]
    [DataRow("zero-lease")]
    [DataRow("missing-lease")]
    public async Task GuestDoesNotBindAnInvalidAcknowledgement(string fault)
    {
        var network = new VirtualDhcpNetwork(new() { TransformReply = packet =>
        {
            Assert.IsTrue(DhcpPacket.TryRead(packet.Payload.Span, out DhcpMessage message));
            if (message.Type != DhcpMessageType.Ack) return [packet];
            var options = message.Options.Select(pair => (pair.Key, pair.Value)).ToList();
            if (fault == "different-server") options[options.FindIndex(o => o.Key == 54)] = (54, DhcpPacket.Word(0xC0000202));
            if (fault == "zero-lease") options[options.FindIndex(o => o.Key == 51)] = (51, new byte[4]);
            if (fault == "missing-lease") options.RemoveAll(o => o.Key == 51);
            byte[] bytes = DhcpPacket.Create(2, message.Transaction, message.HardwareAddress,
                fault == "different-address" ? 0xC000022B : message.OfferedAddress, options);
            return [packet with { Payload = bytes }];
        }});
        DhcpExperimentResult result = await DhcpExperiment.RunRomAsync(network);
        Assert.IsFalse(result.Acquired); Assert.AreEqual(0u, result.Address); Assert.AreEqual(0u, result.LeaseSeconds);
    }

    [TestMethod]
    public async Task AckConfigurationIsAuthoritativeAndRepeatedNakCannotLoopForever()
    {
        var optional = new VirtualDhcpNetwork(new() { TransformReply = packet =>
        {
            Assert.IsTrue(DhcpPacket.TryRead(packet.Payload.Span, out DhcpMessage message));
            if (message.Type != DhcpMessageType.Ack) return [packet];
            byte[] bytes = DhcpPacket.Create(2, message.Transaction, message.HardwareAddress, message.OfferedAddress,
                message.Options.Where(p => p.Key is not (1 or 3 or 6)).Select(p => (p.Key, p.Value)));
            return [packet with { Payload = bytes }];
        }});
        DhcpExperimentResult result = await DhcpExperiment.RunRomAsync(optional);
        Assert.IsTrue(result.Acquired);
        Assert.AreEqual(0u, result.Mask); Assert.AreEqual(0u, result.Router); Assert.AreEqual(0u, result.Dns);
        var repeatedNak = new VirtualDhcpNetwork(new() { TransformReply = packet =>
        {
            Assert.IsTrue(DhcpPacket.TryRead(packet.Payload.Span, out DhcpMessage message));
            if (message.Type != DhcpMessageType.Ack) return [packet];
            byte[] bytes = DhcpPacket.Create(2, message.Transaction, message.HardwareAddress, 0,
                [(53, [(byte)DhcpMessageType.Nak]), (54, DhcpPacket.Word(DhcpPacket.ServerAddress))]);
            return [packet with { Payload = bytes }];
        }});
        Assert.IsFalse((await DhcpExperiment.RunRomAsync(repeatedNak)).Acquired);
        Assert.HasCount(4, repeatedNak.Messages);
    }

    [TestMethod]
    public async Task DatagramModePreservesZeroLengthMessagesEndpointsAndNonDestructiveInspection()
    {
        var backend = new TestDatagrams();
        await using var device = new NetworkCartridgeDevice(backend);
        var machine = new Gameboy(); machine.Load(new Cartridge { Data = DhcpCartridgeRom.Create() });
        machine.Memory.AddDevice(device, takePriority: true);
        device.Write8(NetworkCartridgeDevice.LocalPort, 68);
        Submit(device, NetworkCommand.Connect);
        Assert.AreEqual((ushort)68, backend.Port);
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual(NetworkCartridgeDevice.Connected | NetworkCartridgeDevice.ReceiveReady, device.Peek(NetworkCartridgeDevice.Status));
        Assert.AreEqual((byte)67, machine.Memory.Peek(NetworkCartridgeDevice.SourcePort));
        Assert.AreEqual((byte)192, machine.Memory.Snapshot()[NetworkCartridgeDevice.SourceAddress]);
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual((byte)NetworkError.ReceivePending, device.Peek(NetworkCartridgeDevice.Error));
        Submit(device, NetworkCommand.Acknowledge);
        backend.FailWithTimeout = true;
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual((byte)NetworkError.TimedOut, device.Peek(NetworkCartridgeDevice.Error));
        Assert.AreEqual(NetworkCartridgeDevice.Connected, device.Peek(NetworkCartridgeDevice.Status) & NetworkCartridgeDevice.Connected);
        backend.FailWithTimeout = false; backend.Payload = new byte[1025];
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual((byte)NetworkError.Transport, device.Peek(NetworkCartridgeDevice.Error));
    }

    [TestMethod]
    public async Task GuestCancellationDisposesPendingDatagramReceive()
    {
        var backend = new TestDatagrams { Block = true };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() => DhcpExperiment.RunRomAsync(backend, cancellation.Token));
        Assert.IsTrue(backend.Disposed);
    }

    private static NetworkDatagram Corrupt(NetworkDatagram packet, string fault)
    {
        byte[] bytes = packet.Payload.ToArray();
        if (fault == "xid") bytes[4] ^= 1;
        if (fault == "mac") bytes[28] ^= 1;
        if (fault == "cookie") bytes[236] ^= 1;
        if (fault == "opcode") bytes[0] = 1;
        if (fault == "source-port") return packet with { Source = packet.Source with { Port = 69 } };
        if (fault == "source-address") return packet with { Source = packet.Source with { Address = 0xC0000202 } };
        if (fault == "short-header") return packet with { Payload = bytes[..239] };
        if (fault == "truncated-option") return packet with { Payload = bytes[..242] };
        if (fault == "missing-end") { for (int i = 240; i < bytes.Length; i++) bytes[i] = 0; }
        if (fault is "duplicate-type" or "overload")
        {
            Assert.IsTrue(DhcpPacket.TryRead(bytes, out DhcpMessage message));
            var options = message.Options.Select(pair => (pair.Key, pair.Value)).ToList();
            options.Add(fault == "duplicate-type" ? ((byte)53, new byte[] {2}) : ((byte)52, new byte[] {1}));
            bytes = DhcpPacket.Create(2, message.Transaction, message.HardwareAddress, message.OfferedAddress, options);
        }
        if (fault == "invalid-option-length") bytes[241] = 0;
        return packet with { Payload = bytes };
    }

    private static void AssertLease(DhcpExperimentResult result)
    {
        Assert.IsTrue(result.Acquired); Assert.AreEqual((byte)4, result.State);
        Assert.AreEqual(DhcpPacket.ClientAddress, result.Address); Assert.AreEqual(DhcpPacket.ServerAddress, result.Server);
        Assert.AreEqual(DhcpPacket.SubnetMask, result.Mask); Assert.AreEqual(DhcpPacket.Gateway, result.Router);
        Assert.AreEqual(DhcpPacket.Dns, result.Dns); Assert.AreEqual(DhcpPacket.LeaseSeconds, result.LeaseSeconds);
    }
    private static void Submit(NetworkCartridgeDevice device, NetworkCommand command)
    { device.Write8(NetworkCartridgeDevice.Command, (byte)command); device.Pump(); }

    private sealed class TestDatagrams : IDatagramCartridgeBackend
    {
        public ushort Port;
        public bool FailWithTimeout, Block, Disposed;
        public byte[] Payload = [];
        public ValueTask OpenAsync(ushort localPort, CancellationToken token) { this.Port = localPort; return ValueTask.CompletedTask; }
        public ValueTask SendAsync(DatagramEndpoint destination, ReadOnlyMemory<byte> payload, CancellationToken token) => ValueTask.CompletedTask;
        public async ValueTask<NetworkDatagram> ReceiveAsync(CancellationToken token)
        {
            if (this.Block) await Task.Delay(Timeout.Infinite, token);
            if (this.FailWithTimeout) throw new TimeoutException();
            return new(new(DhcpPacket.ServerAddress, 67), new(uint.MaxValue, this.Port), this.Payload);
        }
        public ValueTask CloseAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { this.Disposed = true; return ValueTask.CompletedTask; }
    }
}
