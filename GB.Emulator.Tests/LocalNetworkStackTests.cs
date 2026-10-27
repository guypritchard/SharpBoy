using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class LocalNetworkStackTests
{
    [TestMethod]
    [DataRow(LocalNetworkTransport.Udp)]
    [DataRow(LocalNetworkTransport.Tcp)]
    public async Task SdkRomAcquiresLeaseAndEmitsItsOwnValuesOverRealSockets(LocalNetworkTransport transport)
    {
        LeaseTelemetry values = await NetworkTelemetryExperiment.RunAsync(transport);
        Assert.AreEqual((byte)1, values.NodeId); Assert.AreEqual(transport, values.Transport);
        Assert.AreEqual(DhcpPacket.ClientAddress, values.Address);
        Assert.AreEqual(DhcpPacket.ServerAddress, values.Server);
        Assert.AreEqual(DhcpPacket.SubnetMask, values.Mask);
        Assert.AreEqual(DhcpPacket.Gateway, values.Router);
        Assert.AreEqual(DhcpPacket.Dns, values.Dns);
        Assert.AreEqual(DhcpPacket.LeaseSeconds, values.LeaseSeconds);
    }

    [TestMethod]
    [DataRow(LocalNetworkTransport.Udp, 4, 1000, 16)]
    [DataRow(LocalNetworkTransport.Tcp, 4, 1000, 1)]
    [DataRow(LocalNetworkTransport.Tcp, 1, 65535, 3)]
    [DataRow(LocalNetworkTransport.Udp, 3, 0, 16)]
    public async Task IndependentNativeWorkersComputeEveryShardIncludingOverflowAndEmptyJobs(
        LocalNetworkTransport transport, int workers, int count, int fragment)
    {
        ClusterResult result = await GameboyNetworkCluster.RunAsync(workers, (ushort)count, transport, 3, fragment);
        Assert.AreEqual(unchecked((ushort)((long)count * (count - 1) / 2)), result.Sum);
        Assert.AreEqual(count, result.Shards.Sum(s => s.Count));
        Assert.HasCount(workers * 3, result.Shards);
        Assert.HasCount(workers, result.Shards.Select(s => s.NodeId).Distinct().ToArray());
        Assert.HasCount(workers * 3, result.Shards.Select(s => s.JobId).Distinct().ToArray());
        Assert.HasCount(workers, result.WorkerCycles);
        Assert.IsTrue(result.WorkerCycles.Values.All(cycles => cycles > 0));
        int cursor = 0;
        foreach (ClusterShard shard in result.Shards)
        {
            Assert.AreEqual(cursor, (int)shard.Start);
            cursor += shard.Count;
            Assert.AreEqual(unchecked((ushort)((long)shard.Count * (2L * shard.Start + shard.Count - 1) / 2)), shard.Sum);
        }
    }

    [TestMethod]
    public async Task UdpWorkerCanReplayAJobAfterItsFirstResultIsLost()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<NativeNodeResult> node = NativeNetworkNode.RunAsync("Worker", 7, LocalNetworkTransport.Udp,
            (ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port, timeout.Token);
        try
        {
            UdpReceiveResult hello = await server.ReceiveAsync(timeout.Token);
            Assert.AreEqual((byte)7, hello.Buffer[4]);
            byte[] job = WorkerRecord(7, 1, 42, 65530, 6);
            await server.SendAsync(job, hello.RemoteEndPoint, timeout.Token);
            byte[] dropped = (await server.ReceiveAsync(timeout.Token)).Buffer;
            await server.SendAsync(job, hello.RemoteEndPoint, timeout.Token);
            byte[] retry = (await server.ReceiveAsync(timeout.Token)).Buffer;
            CollectionAssert.AreEqual(dropped, retry);
            Assert.AreEqual(unchecked((ushort)Enumerable.Range(65530, 6).Sum()), BinaryPrimitives.ReadUInt16LittleEndian(retry.AsSpan(12)));
            Assert.AreEqual((ushort)42, BinaryPrimitives.ReadUInt16LittleEndian(retry.AsSpan(6)));
            await server.SendAsync(WorkerRecord(7, 3, 0, 0, 0), hello.RemoteEndPoint, timeout.Token);
            Assert.AreEqual((byte)1, (await node).Result);
        }
        finally { timeout.Cancel(); try { await node; } catch (OperationCanceledException) { } }
    }

    [TestMethod]
    public async Task NativeWorkerRejectsWrongNodeIdentityWithoutComputing()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<NativeNodeResult> node = NativeNetworkNode.RunAsync("Worker", 3, LocalNetworkTransport.Udp,
            (ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port, timeout.Token);
        try
        {
            UdpReceiveResult hello = await server.ReceiveAsync(timeout.Token);
            await server.SendAsync(WorkerRecord(4, 1, 1, 0, 10), hello.RemoteEndPoint, timeout.Token);
            Assert.AreEqual((byte)255, (await node).Result);
        }
        finally { timeout.Cancel(); try { await node; } catch (OperationCanceledException) { } }
    }

    [TestMethod]
    public async Task RealUdpPreservesEmptyDatagramsAndCancellationAndRefusesLanDestinations()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var backend = new LocalDatagramBackend();
        await backend.OpenAsync(0, timeout.Token);
        var endpoint = new DatagramEndpoint(0x7F000001, (ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
        await backend.SendAsync(endpoint, ReadOnlyMemory<byte>.Empty, timeout.Token);
        UdpReceiveResult empty = await server.ReceiveAsync(timeout.Token);
        Assert.HasCount(0, empty.Buffer);
        await server.SendAsync(new byte[] { 1, 2, 3 }, empty.RemoteEndPoint, timeout.Token);
        NetworkDatagram reply = await backend.ReceiveAsync(timeout.Token);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, reply.Payload.ToArray());
        Assert.AreEqual(endpoint, reply.Source);
        await Assert.ThrowsExactlyAsync<IOException>(() => backend.SendAsync(new(0xC0A80101, 9000), new byte[] { 1 }, timeout.Token).AsTask());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => backend.ReceiveAsync(cancelled.Token).AsTask());
    }

    [TestMethod]
    public async Task DualTransportIsClosedOnlyAndBootstrapIsReadOnly()
    {
        await using var device = new NetworkCartridgeDevice(new LocalDhcpBackend(), new LoopbackNetworkBackend(),
            new byte[] { 9, 1, 127, 0, 0, 1, 1, 0 });
        Assert.AreEqual((byte)2, device.Peek(0xA001)); Assert.AreEqual((byte)3, device.Peek(0xA009));
        device.Write8(NetworkCartridgeDevice.Bootstrap, 123);
        Assert.AreEqual((byte)9, device.Peek(NetworkCartridgeDevice.Bootstrap));
        device.Write8(NetworkCartridgeDevice.Transport, 1);
        device.Write8(NetworkCartridgeDevice.Command, (byte)NetworkCommand.Connect);
        device.Write8(NetworkCartridgeDevice.Transport, 0);
        Assert.AreEqual((byte)NetworkError.Busy, device.Peek(NetworkCartridgeDevice.Error));
        device.Pump();
        device.Write8(NetworkCartridgeDevice.Transport, 0);
        Assert.AreEqual((byte)NetworkError.InvalidCommand, device.Peek(NetworkCartridgeDevice.Error));
        Assert.AreEqual((byte)1, device.Peek(NetworkCartridgeDevice.Transport));
        device.Write8(NetworkCartridgeDevice.Command, (byte)NetworkCommand.Close); device.Pump();
        device.Write8(NetworkCartridgeDevice.Transport, 0);
        Assert.AreEqual((byte)0, device.Peek(NetworkCartridgeDevice.Transport));
        Assert.AreEqual((byte)NetworkError.None, device.Peek(NetworkCartridgeDevice.Error));
    }

    [TestMethod]
    public async Task CancellationStopsTheWholeClusterAndInvalidArgumentsAreRejected()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => GameboyNetworkCluster.RunAsync(token: cancelled.Token));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => GameboyNetworkCluster.RunAsync(workers: 0));
        Assert.AreEqual(2, await LocalNetworkExperiment.RunAsync(["--workers", "0"], cluster: true));
        Assert.AreEqual(2, await LocalNetworkExperiment.RunAsync(["--receiver-port", "0"], cluster: false));
    }

    private static byte[] WorkerRecord(byte node, byte kind, ushort job, ushort start, ushort count)
    {
        byte[] bytes = new byte[16]; "GBW1"u8.CopyTo(bytes); bytes[4] = node; bytes[5] = kind;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), job);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), start);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), count);
        return bytes;
    }
}
