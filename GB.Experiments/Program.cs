using GB.Experiments;

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
