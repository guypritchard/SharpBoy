using GB.Experiments;
using GB.Experiments.NetworkCartridge;

// OpenSSH invokes this apphost as a noninteractive password callback. Credentials
// exist only in the SSH child's environment, never command arguments or host JSON.
if (Environment.GetEnvironmentVariable("GB_LIFE_ASKPASS") == "1")
{
    string? password = Environment.GetEnvironmentVariable("GB_LIFE_ASKPASS_PASSWORD");
    if (args.Length == 0 || string.IsNullOrEmpty(password)) return 2;
    Console.Out.WriteLine(password); return 0;
}

if (args.Length > 0 && args[0] == "--network-cartridge")
    return await NetworkCartridgeExperiment.RunAsync(args[1..]);

if (args is ["--life-worker-group"]) return await LifeWorkerGroup.RunAsync();

if (args.FirstOrDefault() == "--life")
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    try
    {
        var options = new List<string>(); string? report = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--report" && i + 1 < args.Length) report = Path.GetFullPath(args[++i]);
            else options.Add(args[i]);
        }
        var settings = LifeSettings.Parse(options.ToArray());
        var result = await LifeCluster.RunAsync(settings,
            onGeneration: tick => Console.WriteLine($"Generation {tick.Generation}: {tick.Alive:N0} live cells | {tick.ElapsedSeconds:F2}s"), token: stop.Token);
        Console.WriteLine($"Verified {result.Final.Generation} generations from {result.Nodes.Count} native Game Boys over TCP.");
        if (report != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, System.Text.Json.JsonSerializer.Serialize(new
            {
                Settings = settings, result.Final.Generation, result.Final.Width, result.Final.Height, result.Final.Alive,
                result.Final.ElapsedSeconds, result.Metrics,
                WorldSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Final.Cells.Span)),
                Nodes = result.Nodes.Select((n, i) => new { NodeId = i + 1, n.Result, n.Cycles })
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Saved scaling report: " + report);
        }
        return 0;
    }
    catch (OperationCanceledException) { Console.WriteLine("Life experiment stopped."); return 1; }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
}

int port = 8765;
int workers = Math.Min(4, Environment.ProcessorCount);
string? romPath = null;
bool workerMode = false;
bool benchmarkMode = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedPort)
            && parsedPort is >= 0 and <= 65535:
            port = parsedPort;
            i++;
            break;
        case "--rom" when i + 1 < args.Length:
            romPath = args[++i];
            break;
        case "--workers" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedWorkers)
            && parsedWorkers is >= 1 and <= 32:
            workers = parsedWorkers;
            i++;
            break;
        case "--worker":
            workerMode = true;
            break;
        case "--benchmark":
            benchmarkMode = true;
            break;
        default:
            Console.Error.WriteLine("Usage: GB.Experiments [--port 8765] [--workers 4] [--rom path/to/program.gb] [--benchmark]");
            return 2;
    }
}

if (workerMode)
{
    await GameboyWorkerHost.RunAsync(romPath);
    return 0;
}

if (romPath is not null && !File.Exists(romPath))
{
    Console.Error.WriteLine($"ROM not found: {romPath}");
    return 2;
}

if (benchmarkMode)
{
    await WorkerBenchmark.RunAsync(romPath);
    return 0;
}

await using var pool = new GameboyWorkerPool(workers, romPath);
var adapter = new VirtualSerialTcpAdapter(pool, port);
using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.WriteLine($"Started {workers} isolated Game Boy workers (round robin).");
await adapter.RunAsync(shutdown.Token);
return 0;
