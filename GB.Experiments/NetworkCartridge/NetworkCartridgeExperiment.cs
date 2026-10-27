using System.Net;
using System.Net.Sockets;
using System.Text;
using GB.Emulator.Core;

namespace GB.Experiments.NetworkCartridge;

public sealed record NetworkExperimentResult(byte Result, string Reply, long Cycles, NetworkError Error);

public static class NetworkCartridgeExperiment
{
    /// <summary>Owns backend lifetime. The machine and device are deliberately private: live-network rewind is unsupported.</summary>
    public static async Task<NetworkExperimentResult> RunRomAsync(INetworkCartridgeBackend backend, CancellationToken cancellationToken = default)
    {
        await using var device = new NetworkCartridgeDevice(backend);
        var machine = new Gameboy();
        byte[] rom = NetworkCartridgeRom.Create();
        machine.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, Header.Length).ToArray()) });
        machine.Memory.AddDevice(device, takePriority: true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!machine.Cpu.IsHalted)
        {
            timeout.Token.ThrowIfCancellationRequested();
            device.Pump();
            machine.RunForCycles(4096, maxInstructions: 1024);
            if (device.HasPendingOperation) await Task.Delay(1, timeout.Token);
        }
        byte[] reply = new byte[Encoding.ASCII.GetByteCount(NetworkCartridgeRom.Message)];
        for (int i = 0; i < reply.Length; i++) reply[i] = machine.Memory.Peek((ushort)(NetworkCartridgeRom.ReplyAddress + i));
        return new(machine.Memory.Peek(NetworkCartridgeRom.ResultAddress), Encoding.ASCII.GetString(reply), machine.EmulatedCycles,
            (NetworkError)device.Peek(NetworkCartridgeDevice.Error));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && args[0] == "--dhcp") return await DhcpExperiment.RunAsync(args[1..]);
        if (args.Length > 0 && args[0] == "--telemetry") return await LocalNetworkExperiment.RunAsync(args[1..], cluster: false);
        if (args.Length > 0 && args[0] == "--cluster") return await LocalNetworkExperiment.RunAsync(args[1..], cluster: true);
        if (args.Length > 0 && args[0] == "--receive") return await LocalNetworkExperiment.RunReceiverAsync(args[1..]);
        bool tcp = false;
        string? output = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--tcp") tcp = true;
            else if (args[i] == "--rom-output" && i + 1 < args.Length) output = args[++i];
            else { Console.Error.WriteLine("Usage: GB.Experiments --network-cartridge [--tcp] [--rom-output demo.gb]"); return 2; }
        }
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (output != null)
            {
                string path = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, NetworkCartridgeRom.Create(), shutdown.Token);
            }
            NetworkExperimentResult result;
            if (tcp)
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine($"Network cartridge → TCP echo server at 127.0.0.1:{port}");
                using var serverLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                Task server = EchoOnceAsync(listener, serverLifetime.Token);
                try { result = await RunRomAsync(new TcpNetworkBackend(port), shutdown.Token); }
                finally
                {
                    serverLifetime.Cancel(); listener.Stop();
                    try { await server; }
                    catch (OperationCanceledException) when (serverLifetime.IsCancellationRequested) { }
                }
            }
            else
            {
                Console.WriteLine("Network cartridge → deterministic loopback (7-byte receive fragments)");
                result = await RunRomAsync(new LoopbackNetworkBackend(), shutdown.Token);
            }
            Console.WriteLine($"ROM result: {(result.Result == 1 ? "PASS" : "FAIL")}; device error: {result.Error}; cycles: {result.Cycles}");
            Console.WriteLine($"Echo: {result.Reply.TrimEnd()}");
            return result.Result == 1 && result.Reply == NetworkCartridgeRom.Message ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Network experiment cancelled or timed out."); return 1; }
        catch (Exception ex) when (ex is IOException or SocketException)
        { Console.Error.WriteLine($"Network experiment failed: {ex.Message}"); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static async Task EchoOnceAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = client.GetStream();
        var buffer = new byte[5];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
    }
}
