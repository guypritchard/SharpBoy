using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GB.Emulator.Core;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class NetworkCartridgeTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    [DataRow(256)]
    public async Task RomConnectsSendsCollectsFragmentsVerifiesAndCloses(int fragmentSize)
    {
        NetworkExperimentResult result = await NetworkCartridgeExperiment.RunRomAsync(new LoopbackNetworkBackend(fragmentSize));
        Assert.AreEqual((byte)1, result.Result);
        Assert.AreEqual(NetworkCartridgeRom.Message, result.Reply);
        Assert.AreEqual(NetworkError.None, result.Error);
        Assert.IsGreaterThan(0L, result.Cycles);
    }

    [TestMethod]
    public async Task RomUsesRealLoopbackTcpThroughCommandLineExperiment()
    {
        Assert.AreEqual(0, await NetworkCartridgeExperiment.RunAsync(["--tcp"]));
        Assert.AreEqual(2, await NetworkCartridgeExperiment.RunAsync(["--unknown"]));
    }

    [TestMethod]
    public async Task PeripheralOverridesOnlyItsWindowAndInspectionDoesNotAcknowledgeData()
    {
        await using var device = new NetworkCartridgeDevice(new LoopbackNetworkBackend());
        var machine = new Gameboy();
        machine.Load(new Cartridge { Data = NetworkCartridgeRom.Create() });
        machine.Memory.AddDevice(device, takePriority: true);
        machine.Memory.Write8(0x55, 0xC100);
        Assert.AreEqual((byte)'N', machine.Memory.Read8(0xA000));
        Assert.AreEqual((byte)'N', machine.Memory.Peek(0xA000));
        Assert.AreEqual((byte)0x55, machine.Memory.Peek(0xC100));
        Submit(device, NetworkCommand.Connect);
        machine.Memory.Write8(0x42, NetworkCartridgeDevice.TxBuffer);
        machine.Memory.Write8(1, NetworkCartridgeDevice.TxLength);
        Submit(device, NetworkCommand.Send);
        Submit(device, NetworkCommand.Receive);
        byte status = device.Peek(NetworkCartridgeDevice.Status);
        Assert.AreEqual((byte)0x42, machine.Memory.Peek(NetworkCartridgeDevice.RxBuffer));
        Assert.AreEqual((byte)0x42, machine.Memory.Snapshot()[NetworkCartridgeDevice.RxBuffer]);
        Assert.AreEqual((byte)0x42, machine.Memory.Read8(NetworkCartridgeDevice.RxBuffer));
        Assert.AreEqual(status, device.Peek(NetworkCartridgeDevice.Status));
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual((byte)NetworkError.ReceivePending, device.Peek(NetworkCartridgeDevice.Error));
        Submit(device, NetworkCommand.Acknowledge);
        Assert.AreEqual((byte)0, device.Peek(NetworkCartridgeDevice.RxLength));
        Assert.AreEqual((byte)0, machine.Memory.Peek(NetworkCartridgeDevice.RxBuffer));
    }

    [TestMethod]
    public async Task SendOwnsSubmittedBytesAndRejectsInvalidLengthsAndDisconnectedRequests()
    {
        await using var device = new NetworkCartridgeDevice(new LoopbackNetworkBackend(256));
        Submit(device, NetworkCommand.Send);
        Assert.AreEqual((byte)NetworkError.NotConnected, device.Peek(NetworkCartridgeDevice.Error));
        Submit(device, NetworkCommand.Connect);
        Submit(device, NetworkCommand.Send);
        Assert.AreEqual((byte)NetworkError.InvalidLength, device.Peek(NetworkCartridgeDevice.Error));
        device.Write8(NetworkCartridgeDevice.TxLength, 1);
        device.Write8(NetworkCartridgeDevice.TxLength + 1, 1); // 257 > capacity
        Submit(device, NetworkCommand.Send);
        Assert.AreEqual((byte)NetworkError.InvalidLength, device.Peek(NetworkCartridgeDevice.Error));
        device.Write8(NetworkCartridgeDevice.TxLength + 1, 0);
        device.Write8(NetworkCartridgeDevice.TxBuffer, 0x42);
        device.Write8(NetworkCartridgeDevice.Command, (byte)NetworkCommand.Send);
        device.Write8(NetworkCartridgeDevice.TxBuffer, 0x99); // queued command must retain 0x42
        device.Pump();
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual((byte)0x42, device.Read8(NetworkCartridgeDevice.RxBuffer));
        device.Write8(NetworkCartridgeDevice.Command, 0xFF);
        Assert.AreEqual((byte)NetworkError.InvalidCommand, device.Peek(NetworkCartridgeDevice.Error));
    }

    [TestMethod]
    public async Task PendingTransportNeverBlocksBusAndOnlyPumpPublishesCompletion()
    {
        var backend = new ControlledBackend();
        await using var device = new NetworkCartridgeDevice(backend);
        device.Write8(NetworkCartridgeDevice.Command, (byte)NetworkCommand.Connect);
        device.Pump();
        Assert.IsTrue(device.HasPendingOperation);
        device.Write8(NetworkCartridgeDevice.Command, (byte)NetworkCommand.Close);
        Assert.AreEqual((byte)NetworkError.Busy, device.Peek(NetworkCartridgeDevice.Error));
        backend.Connection.SetResult();
        await backend.Returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, device.Peek(NetworkCartridgeDevice.Status) & NetworkCartridgeDevice.Connected);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (device.HasPendingOperation) { device.Pump(); await Task.Delay(1, deadline.Token); }
        Assert.AreEqual(NetworkCartridgeDevice.Connected, device.Peek(NetworkCartridgeDevice.Status));
    }

    [TestMethod]
    public async Task DisposalCancelsPendingIoAndDisposesBackendOnce()
    {
        var backend = new ControlledBackend();
        var device = new NetworkCartridgeDevice(backend);
        device.Write8(NetworkCartridgeDevice.Command, (byte)NetworkCommand.Connect);
        device.Pump();
        await device.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await device.DisposeAsync();
        Assert.AreEqual(1, backend.DisposeCount);
        Assert.ThrowsExactly<ObjectDisposedException>(() => device.Write8(NetworkCartridgeDevice.Command, 1));
    }

    [TestMethod]
    public async Task TransportFailureBecomesDeviceErrorAndRomRejectsCorruptEcho()
    {
        var backend = new ControlledBackend();
        await using var device = new NetworkCartridgeDevice(backend);
        backend.Connection.SetException(new IOException("Simulated connection failure"));
        Submit(device, NetworkCommand.Connect);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (device.HasPendingOperation) { device.Pump(); await Task.Delay(1, deadline.Token); }
        Assert.AreEqual((byte)NetworkError.Transport, device.Peek(NetworkCartridgeDevice.Error));
        Assert.AreEqual(NetworkCartridgeDevice.Failed, device.Peek(NetworkCartridgeDevice.Status));
        var corrupt = new CorruptBackend();
        NetworkExperimentResult result = await NetworkCartridgeExperiment.RunRomAsync(corrupt);
        Assert.AreEqual((byte)0xFF, result.Result);
    }

    [TestMethod]
    public async Task FullBufferEofAndReconnectHaveDefinedLengthsAndConnectionState()
    {
        await using var device = new NetworkCartridgeDevice(new LoopbackNetworkBackend(256));
        Submit(device, NetworkCommand.Connect);
        for (int i = 0; i < 256; i++) device.Write8((ushort)(NetworkCartridgeDevice.TxBuffer + i), (byte)i);
        device.Write8(NetworkCartridgeDevice.TxLength, 0);
        device.Write8(NetworkCartridgeDevice.TxLength + 1, 1);
        Submit(device, NetworkCommand.Send);
        Submit(device, NetworkCommand.Receive);
        Assert.AreEqual((byte)0, device.Peek(NetworkCartridgeDevice.RxLength));
        Assert.AreEqual((byte)1, device.Peek(NetworkCartridgeDevice.RxLength + 1));
        for (int i = 0; i < 256; i++) Assert.AreEqual((byte)i, device.Peek((ushort)(NetworkCartridgeDevice.RxBuffer + i)));
        Submit(device, NetworkCommand.Connect);
        Assert.AreEqual((byte)0, device.Peek(NetworkCartridgeDevice.RxLength + 1));
        Assert.AreEqual(NetworkCartridgeDevice.Connected, device.Peek(NetworkCartridgeDevice.Status));
        Submit(device, NetworkCommand.Receive); // empty echo queue produces EOF
        Assert.AreEqual((byte)0, device.Peek(NetworkCartridgeDevice.Status));
    }

    [TestMethod]
    public async Task CancellingRomRunDisposesPendingTransport()
    {
        var backend = new ControlledBackend();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() => NetworkCartridgeExperiment.RunRomAsync(backend, cancellation.Token));
        Assert.AreEqual(1, backend.DisposeCount);
    }

    [TestMethod]
    public void DemoHasValidRomHeaderAndChecksums()
    {
        byte[] rom = NetworkCartridgeRom.Create();
        Assert.HasCount(0x8000, rom);
        Assert.AreEqual((byte)0, rom[0x147]);
        byte checksum = 0;
        for (int i = 0x134; i <= 0x14C; i++) checksum = unchecked((byte)(checksum - rom[i] - 1));
        Assert.AreEqual(checksum, rom[0x14D]);
        Assert.AreEqual(rom.Where((_, i) => i != 0x14E && i != 0x14F).Sum(b => (int)b) & 0xFFFF, rom[0x14E] << 8 | rom[0x14F]);
    }

    private static void Submit(NetworkCartridgeDevice device, NetworkCommand command)
    { device.Write8(NetworkCartridgeDevice.Command, (byte)command); device.Pump(); }

    private sealed class ControlledBackend : INetworkCartridgeBackend
    {
        public readonly TaskCompletionSource Connection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount;
        public async ValueTask ConnectAsync(CancellationToken token)
        { await this.Connection.Task.WaitAsync(token); this.Returned.TrySetResult(); }
        public ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token) => ValueTask.FromResult(0);
        public ValueTask CloseAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() { this.DisposeCount++; return ValueTask.CompletedTask; }
    }

    private sealed class CorruptBackend : INetworkCartridgeBackend
    {
        public ValueTask ConnectAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token)
        { buffer.Span[..NetworkCartridgeRom.Message.Length].Fill((byte)'?'); return ValueTask.FromResult(NetworkCartridgeRom.Message.Length); }
        public ValueTask CloseAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
