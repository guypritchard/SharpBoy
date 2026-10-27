using System.Net.Sockets;

namespace GB.Experiments.NetworkCartridge;

public static class LocalNetworkExperiment
{
    public static async Task<int> RunReceiverAsync(string[] args)
    {
        LocalNetworkTransport transport = LocalNetworkTransport.Udp;
        ushort port = 9000;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--tcp") transport = LocalNetworkTransport.Tcp;
            else if (args[i] == "--udp") transport = LocalNetworkTransport.Udp;
            else if (args[i] == "--port" && i + 1 < args.Length && ushort.TryParse(args[++i], out ushort parsed) && parsed != 0) port = parsed;
            else { Console.Error.WriteLine("Usage: --network-cartridge --receive [--udp|--tcp] [--port 9000]"); return 2; }
        }
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            using var listener = new LocalRecordListener(transport, port);
            Console.WriteLine($"Waiting for one GBT1 record on 127.0.0.1:{listener.Port} ({transport}); Ctrl+C exits.");
            LeaseTelemetry record = LeaseTelemetry.Decode(await listener.ReceiveAsync(32, shutdown.Token));
            if (record.Transport != transport) throw new InvalidDataException("Telemetry transport does not match the receiving socket.");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(record, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Received IP {DhcpExperiment.FormatAddress(record.Address)} from GB{record.NodeId:D2}.");
            return 0;
        }
        catch (OperationCanceledException) { return 1; }
        catch (Exception ex) when (ex is IOException or SocketException) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }

    public static async Task<int> RunAsync(string[] args, bool cluster)
    {
        LocalNetworkTransport transport = cluster ? LocalNetworkTransport.Tcp : LocalNetworkTransport.Udp;
        int workers = 4, jobs = 4, fragments = 16;
        ushort count = 1000, receiverPort = 0;
        string? output = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tcp": transport = LocalNetworkTransport.Tcp; break;
                case "--udp": transport = LocalNetworkTransport.Udp; break;
                case "--workers" when cluster && i + 1 < args.Length && int.TryParse(args[++i], out int n) && n is >= 1 and <= 16: workers = n; break;
                case "--jobs-per-worker" when cluster && i + 1 < args.Length && int.TryParse(args[++i], out int j) && j is >= 1 and <= 32: jobs = j; break;
                case "--count" when cluster && i + 1 < args.Length && ushort.TryParse(args[++i], out ushort c): count = c; break;
                case "--tcp-fragment-size" when cluster && i + 1 < args.Length && int.TryParse(args[++i], out int f) && f is >= 1 and <= 16: fragments = f; break;
                case "--receiver-port" when !cluster && i + 1 < args.Length && ushort.TryParse(args[++i], out ushort p) && p != 0: receiverPort = p; break;
                case "--rom-output" when i + 1 < args.Length: output = args[++i]; break;
                default:
                    Console.Error.WriteLine("Usage: --network-cartridge --telemetry [--udp|--tcp] [--receiver-port PORT] [--rom-output file.gb]\n       --network-cartridge --cluster [--udp|--tcp] [--workers 1..16] [--jobs-per-worker 1..32] [--count 0..65535] [--tcp-fragment-size 1..16] [--rom-output file.gb]");
                    return 2;
            }
        }
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (output != null)
            {
                string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, NativeNetworkNode.Firmware(cluster ? "Worker" : "Telemetry"), shutdown.Token);
            }
            if (cluster)
            {
                ClusterResult result = await GameboyNetworkCluster.RunAsync(workers, count, transport, jobs, fragments, shutdown.Token);
                foreach (ClusterShard shard in result.Shards)
                    Console.WriteLine($"GB{shard.NodeId:D2} job {shard.JobId}: start={shard.Start} count={shard.Count} sum16={shard.Sum}");
                Console.WriteLine($"PASS: {workers} independent SM83 nodes over localhost {transport}; sum(0..{count}-1) mod 65536 = {result.Sum}");
                foreach (var worker in result.WorkerCycles.OrderBy(p => p.Key)) Console.WriteLine($"GB{worker.Key:D2}: {worker.Value} emulated cycles");
            }
            else
            {
                LeaseTelemetry telemetry;
                if (receiverPort == 0) telemetry = await NetworkTelemetryExperiment.RunAsync(transport, shutdown.Token);
                else
                {
                    NativeNodeResult node = await NativeNetworkNode.RunAsync("Telemetry", 1, transport, receiverPort, shutdown.Token);
                    if (node.Result != 1) throw new IOException("The guest could not send telemetry to the selected localhost receiver.");
                    telemetry = LeaseTelemetry.Decode(node.Mailbox.AsSpan(0x70, 32));
                }
                Console.WriteLine(receiverPort == 0 ? $"PASS: native DHCP lease record received over localhost {transport}" : $"Guest send completed to 127.0.0.1:{receiverPort} over {transport}; receiver delivery is not acknowledged.");
                Console.WriteLine($"GB{telemetry.NodeId:D2}: IP {DhcpExperiment.FormatAddress(telemetry.Address)}, mask {DhcpExperiment.FormatAddress(telemetry.Mask)}, gateway {DhcpExperiment.FormatAddress(telemetry.Router)}, DNS {DhcpExperiment.FormatAddress(telemetry.Dns)}, server {DhcpExperiment.FormatAddress(telemetry.Server)}, lease {telemetry.LeaseSeconds}s");
                Console.WriteLine("The lease is virtual; the actual host socket uses 127.0.0.1, not the leased address.");
            }
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Local network experiment cancelled or timed out."); return 1; }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException or ArgumentException)
        { Console.Error.WriteLine(ex.Message); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
