using System.Diagnostics;
using GB.Emulator.Core;
using GB.Emulator.Hosting;

namespace GB.Experiments.NetworkCartridge;

public enum LocalNetworkTransport : byte { Udp, Tcp }
public sealed record NativeNodeResult(byte Result, byte[] Mailbox, long Cycles);

/// <summary>One private SM83 machine per node; its task serializes CPU, I/O pumping and inspection.</summary>
public static class NativeNetworkNode
{
    public static byte[] Firmware(string name)
    {
        using var stream = typeof(NativeNetworkNode).Assembly.GetManifestResourceStream($"GB.Experiments.Network{name}.gb")
            ?? throw new ArgumentException("Unknown network firmware.", nameof(name));
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }

    public static async Task<NativeNodeResult> RunAsync(string firmware, ushort nodeId, LocalNetworkTransport transport,
        ushort receiverPort, CancellationToken token = default, Action<VideoFrame>? onFrame = null, TimeSpan? runTimeout = null,
        ReadOnlyMemory<byte> extraBootstrap = default, Action<NativeNodeSnapshot>? onSnapshot = null, TimeSpan? frameInterval = null,
        Func<bool>? wantsFrames = null, TimeSpan? networkTimeout = null, TimeSpan? inspectionInterval = null, bool parkDuringNetworkIo = false)
    {
        if (receiverPort == 0 || !Enum.IsDefined(transport)) throw new ArgumentOutOfRangeException(nameof(receiverPort));
        if (extraBootstrap.Length > 8) throw new ArgumentException("Extra bootstrap settings exceed eight bytes.", nameof(extraBootstrap));
        byte[] bootstrap = [(byte)nodeId, (byte)transport, 127, 0, 0, 1, (byte)receiverPort, (byte)(receiverPort >> 8), 0, 0, 0, 0, 0, 0, 0, 0];
        extraBootstrap.Span.CopyTo(bootstrap.AsSpan(8));
        if (nodeId > 255) bootstrap[15] = (byte)(nodeId >> 8);
        var tcp = new TcpNetworkBackend(receiverPort);
        await using var device = new NetworkCartridgeDevice(new LocalDhcpBackend(), tcp, bootstrap, networkTimeout);
        var machine = new Gameboy();
        using var host = new EmulationHost(machine, options: new() { HistoryCapacity = 0, CaptureFrames = onFrame != null && frameInterval == null });
        byte[] rom = Firmware(firmware);
        host.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, Header.Length).ToArray()) });
        machine.Memory.AddDevice(device, takePriority: true); host.Resume();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(runTimeout ?? TimeSpan.FromSeconds(30));
        var wall = Stopwatch.StartNew();
        double lastSample = 0, lastFrame = -1;
        long lastCycles = 0;
        void Publish(string state)
        {
            if (onSnapshot == null) return;
            double seconds = wall.Elapsed.TotalSeconds;
            long cycles = machine.EmulatedCycles;
            double hz = (cycles - lastCycles) / Math.Max(seconds - lastSample, 0.000001);
            int nonZero = 0;
            for (int i = 0; i < 8192; i++) if (machine.Memory.Peek((ushort)(0xC000 + i)) != 0) nonZero++;
            onSnapshot(new(nodeId, state, seconds, cycles, hz, host.Inspection.CaptureCpu(),
                host.Inspection.CaptureHardware(), host.Inspection.CaptureCartridge()!, nonZero,
                transport, tcp.CaptureStatistics(), device.Peek(NetworkCartridgeDevice.Status),
                (NetworkError)device.Peek(NetworkCartridgeDevice.Error), device.BufferCapacity));
            lastSample = seconds; lastCycles = cycles;
        }
        int batches = 0;
        VideoFrame? presented = null;
        try
        {
            while (machine.Memory.Peek(0xC060) is not (1 or 255))
            {
                timeout.Token.ThrowIfCancellationRequested();
                device.Pump();
                if (parkDuringNetworkIo && device.HasPendingOperation)
                {
                    // Experiment scheduling policy: suspend the guest clock while offloaded socket I/O waits.
                    // The machine remains owned by this task; only Pump publishes transport completions.
                    if (wall.Elapsed.TotalSeconds - lastSample >= (inspectionInterval ?? TimeSpan.FromMilliseconds(250)).TotalSeconds) Publish("Running");
                    await device.WaitForPendingOperationAsync(timeout.Token); continue;
                }
                host.Advance(TimeSpan.FromSeconds(1.0 / 60));
                if (onFrame != null && frameInterval != null && (wantsFrames?.Invoke() ?? true) &&
                    wall.Elapsed.TotalSeconds - lastFrame >= frameInterval.Value.TotalSeconds && (machine.Memory.Peek(0xFF40) & 0x80) != 0)
                { onFrame(host.Inspection.CaptureFrame()); lastFrame = wall.Elapsed.TotalSeconds; }
                if (onFrame != null && frameInterval == null && host.LatestFrame is { } frame && !ReferenceEquals(frame, presented))
                { presented = frame; onFrame(frame); }
                if (wall.Elapsed.TotalSeconds - lastSample >= (inspectionInterval ?? TimeSpan.FromMilliseconds(250)).TotalSeconds) Publish("Running");
                if (host.State == EmulationState.Faulted) throw new IOException("Native network worker faulted.", host.Fault);
                if (device.HasPendingOperation) await Task.Delay(1, timeout.Token);
                else if (++batches % 16 == 0) await Task.Yield();
            }
            timeout.Token.ThrowIfCancellationRequested();
            if (onFrame != null)
            {
                host.Advance(TimeSpan.FromSeconds(1.0 / 30));
                if (frameInterval != null) onFrame(host.Inspection.CaptureFrame());
                else if (host.LatestFrame is { } finalFrame) onFrame(finalFrame);
            }
            byte[] mailbox = new byte[0x100];
            for (int i = 0; i < mailbox.Length; i++) mailbox[i] = machine.Memory.Peek((ushort)(0xC000 + i));
            Publish(mailbox[0x60] == 1 ? "Completed" : "Failed");
            return new(mailbox[0x60], mailbox, machine.EmulatedCycles);
        }
        catch (OperationCanceledException) { Publish("Stopped"); throw; }
        catch { Publish("Failed"); throw; }
    }
}
