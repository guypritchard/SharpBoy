using System.Buffers.Binary;
using System.Diagnostics;
using GB.Emulator.Hosting;

namespace GB.Experiments.NetworkCartridge;

public sealed record LifeSettings(int Columns = 4, int Rows = 4, int Generations = 80, int TickDelayMs = 50, int Seed = 314159, int WorkerProcesses = -1, string? HostsFile = null)
{
    public LifeLayout Layout => LifeLayout.ForGrid(Columns, Rows);
    public int ProcessCount => WorkerProcesses < 0 ? (Layout.Workers > 64 ? Math.Min(16, (Layout.Workers + 63) / 64) : 0) : Math.Min(WorkerProcesses, Layout.Workers);
    public void Validate()
    {
        _ = Layout;
        if (WorkerProcesses is < -1 or > 32) throw new ArgumentOutOfRangeException(nameof(WorkerProcesses), "Use -1 for automatic grouping, 0 for in-process, or 1-32 worker processes.");
        if (Generations is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(Generations), "Use 1-1,000 generations.");
        if (TickDelayMs is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(TickDelayMs), "Use a tick delay of 0-1,000 ms.");
    }
    public static LifeSettings Parse(string[] args)
    {
        var settings = new LifeSettings();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (args[i] == "--hosts" && i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
            { settings = settings with { HostsFile = Path.GetFullPath(args[i + 1]) }; continue; }
            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out int value)) throw new ArgumentException("Life options require whole-number values.");
            settings = args[i] switch
            {
                "--columns" => settings with { Columns = value }, "--rows" => settings with { Rows = value },
                "--generations" => settings with { Generations = value }, "--tick-delay-ms" => settings with { TickDelayMs = value },
                "--seed" => settings with { Seed = value }, "--processes" => settings with { WorkerProcesses = value },
                _ => throw new ArgumentException("Usage: --life [--columns 4] [--rows 4] [--generations 80] [--tick-delay-ms 50] [--seed 314159] [--processes -1] [--hosts hosts.json]")
            };
        }
        settings.Validate(); return settings;
    }
}

public sealed record LifeLayout(int Workers, int Columns, int Rows)
{
    public int Width => Columns * LifeCluster.TileWidth;
    public int Height => Rows * LifeCluster.TileHeight;
    public static LifeLayout ForGrid(int columns, int rows)
    {
        if (columns is < 1 or > 1000 || rows is < 1 or > 1000 || columns * rows > 1000)
            throw new ArgumentOutOfRangeException(nameof(columns), "Use positive grid dimensions with at most 1,000 Game Boys.");
        return new(columns * rows, columns, rows);
    }
}

public sealed record LifeGeneration(int Generation, int Width, int Height, ReadOnlyMemory<byte> Cells, int Alive, double ElapsedSeconds, long WorkerMemoryBytes = 0, int WorkerProcesses = 0);
public sealed record LifeClusterResult(LifeSettings Settings, LifeGeneration Final, IReadOnlyList<NativeNodeResult> Nodes, LifeRunMetrics? Metrics = null);
public sealed record LifeWorkerLocation(string Host, int ProcessId, bool Remote);
public sealed record LifeRunMetrics(int WorkerProcesses, int[] WorkerProcessIds, double CoordinatorCpuSeconds,
    double WorkerCpuSeconds, long CoordinatorPeakMemoryBytes, long SumWorkerPeakMemoryBytes, LifeWorkerLocation[]? Locations = null);

/// <summary>Barrier-synchronized native Conway workers. TCP initializes territories, then routes boundary cells.
/// The reference implementation validates results, but only received guest cells form the next world.</summary>
public static class LifeCluster
{
    public const int TileWidth = 32, TileHeight = 24, HaloBytes = 34 * 26;
    public static async Task<LifeClusterResult> RunAsync(LifeSettings settings,
        Action<LifeGeneration>? onGeneration = null, Action<ushort, VideoFrame>? onFrame = null,
        Action<NativeNodeSnapshot>? onSnapshot = null, CancellationToken token = default,
        ReadOnlyMemory<byte>? initialWorld = null, int fragmentSize = 1024, Func<ushort, bool>? wantsFrames = null)
    {
        token.ThrowIfCancellationRequested();
        settings.Validate(); var layout = settings.Layout;
        var placement = settings.HostsFile == null ? null : LifeHostConfiguration.Load(settings.HostsFile, layout.Workers);
        if (fragmentSize is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(fragmentSize));
        byte[] world = initialWorld?.ToArray() ?? CreateSeed(settings);
        if (world.Length != layout.Width * layout.Height || world.Any(v => v > 1)) throw new ArgumentException("World must contain one 0/1 byte per cell.", nameof(initialWorld));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        var listeners = new List<LocalRecordListener>();
        var nodes = new List<Task<NativeNodeResult>>();
        var wall = Stopwatch.StartNew();
        Task? allNodes = null;
        Exception? nodeFailure = null;
        LifeProcessPool? pool = null;
        using var coordinator = Process.GetCurrentProcess();
        double cpuStart = coordinator.TotalProcessorTime.TotalSeconds;
        LifeGeneration Publish(int generation)
        {
            var snapshot = new LifeGeneration(generation, layout.Width, layout.Height, (byte[])world.Clone(), world.Sum(v => (int)v), wall.Elapsed.TotalSeconds, pool?.WorkingSetBytes ?? 0, pool?.Count ?? 0);
            onGeneration?.Invoke(snapshot); return snapshot;
        }
        try
        {
            for (int i = 0; i < layout.Workers; i++) listeners.Add(new LocalRecordListener(LocalNetworkTransport.Tcp));
            if (placement != null || settings.ProcessCount > 0)
            {
                pool = await LifeProcessPool.StartAsync(listeners, settings.ProcessCount, onFrame, onSnapshot, wantsFrames, lifetime.Token, placement);
                nodes.AddRange(pool.Nodes);
            }
            else for (int index = 0; index < layout.Workers; index++)
            {
                ushort id = (ushort)(index + 1);
                var listener = listeners[index];
                nodes.Add(Task.Run(() => NativeNetworkNode.RunAsync("Life", id, LocalNetworkTransport.Tcp, listener.Port,
                    lifetime.Token, onFrame == null ? null : frame => onFrame(id, frame), TimeSpan.FromMinutes(10), onSnapshot: onSnapshot, frameInterval: TimeSpan.FromMilliseconds(250),
                    wantsFrames: () => wantsFrames?.Invoke(id) ?? true, networkTimeout: TimeSpan.FromMinutes(2), inspectionInterval: TimeSpan.FromSeconds(1), parkDuringNetworkIo: true), lifetime.Token));
            }
            // Monitor failures independently of a stalled barrier; abort every socket and machine together.
            async Task MonitorNode(Task<NativeNodeResult> task)
            {
                try { if ((await task).Result != 1) throw new IOException("Native Life worker failed."); }
                catch (Exception ex) { Interlocked.CompareExchange(ref nodeFailure, ex, null); lifetime.Cancel(); throw; }
            }
            allNodes = Task.WhenAll(nodes.Select(MonitorNode));
            // Observe monitor exceptions even if a socket failure causes us to leave before awaiting it.
            _ = allNodes.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            await Task.WhenAll(listeners.Select(async (listener, index) =>
            {
                byte[] hello = await listener.ReceiveAsync(16, lifetime.Token);
                ValidateHeader(hello, (ushort)(index + 1), 0, 0, 0);
            }));
            LifeGeneration final = Publish(0);
            for (int generation = 1; generation <= settings.Generations; generation++)
            {
                byte[] expected = ReferenceStep(world, layout.Width, layout.Height);
                byte[] next = new byte[world.Length];
                await Task.WhenAll(listeners.Select(async (listener, index) =>
                {
                    ushort id = (ushort)(index + 1);
                    byte[] payload = generation == 1 ? BuildHalo(world, layout, index) : BuildBoundary(world, layout, index);
                    byte[] request = new byte[16 + payload.Length]; Header(id, 1, generation, payload.Length).CopyTo(request, 0);
                    payload.CopyTo(request, 16);
                    await listener.SendAsync(request, fragmentSize, lifetime.Token);
                    byte[] result = await listener.ReceiveAsync(16 + TileWidth * TileHeight, lifetime.Token);
                    ValidateHeader(result, id, 2, generation, TileWidth * TileHeight);
                    int originX = index % layout.Columns * TileWidth, originY = index / layout.Columns * TileHeight;
                    for (int y = 0; y < TileHeight; y++) for (int x = 0; x < TileWidth; x++)
                    {
                        int address = (originY + y) * layout.Width + originX + x;
                        byte cell = result[16 + y * TileWidth + x];
                        if (cell != expected[address]) throw new InvalidDataException($"GB {id}, generation {generation}, cell {x}:{y} differs from reference.");
                        next[address] = cell;
                    }
                }));
                world = next; final = Publish(generation);
                if (settings.TickDelayMs != 0) await Task.Delay(settings.TickDelayMs, lifetime.Token);
            }
            await Task.WhenAll(listeners.Select((listener, index) => listener.SendAsync(Header((ushort)(index + 1), 3, settings.Generations + 1, 0), fragmentSize, lifetime.Token)));
            await allNodes;
            if (pool != null) await pool.DrainAsync(lifetime.Token);
            // OpenSSH waits for forwarded TCP channels as well as the remote command. All
            // native machines have finished; close collectors before waiting for SSH exit.
            foreach (var listener in listeners) listener.Dispose();
            if (pool != null) await pool.CompleteAsync(lifetime.Token);
            coordinator.Refresh();
            return new(settings, final, await Task.WhenAll(nodes), new(pool?.Count ?? 0, pool?.ProcessIds ?? [],
                coordinator.TotalProcessorTime.TotalSeconds - cpuStart, pool?.CpuSeconds ?? 0, coordinator.PeakWorkingSet64, pool?.PeakWorkingSetBytes ?? 0, pool?.Locations ?? []));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("A Life worker failed or timed out before its generation barrier completed. " + nodeFailure?.Message, nodeFailure ?? allNodes?.Exception?.GetBaseException());
        }
        finally
        {
            lifetime.Cancel();
            // Request cooperative cancellation before closing TCP collectors. Otherwise an
            // EOF can reach a guest first and turn a user stop into a transport failure.
            if (pool != null) await pool.StopAsync();
            // Observe every worker before disposing the shared cancellation source.
            try { await Task.WhenAll(nodes); } catch { /* original protocol/cancellation exception is preserved */ }
            foreach (var listener in listeners) listener.Dispose();
            if (pool != null) await pool.DisposeAsync();
        }
    }

    internal static byte[] Header(ushort id, byte kind, int generation, int length)
    {
        byte[] header = new byte[16]; "GBL2"u8.CopyTo(header); BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), id); header[14] = kind;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)generation);
        if (kind is 1 or 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), TileWidth);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), TileHeight);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)length);
        }
        return header;
    }
    private static void ValidateHeader(byte[] record, ushort id, byte kind, int generation, int length)
    {
        if (record.Length != 16 + length || !record.AsSpan(0, 16).SequenceEqual(Header(id, kind, generation, length)))
            throw new InvalidDataException($"Invalid Life record from GB {id}.");
    }
    public static byte[] BuildHalo(ReadOnlySpan<byte> world, LifeLayout layout, int index)
    {
        if (world.Length != layout.Width * layout.Height || index < 0 || index >= layout.Workers) throw new ArgumentException("Invalid tile/world.");
        byte[] halo = new byte[HaloBytes];
        int ox = index % layout.Columns * TileWidth, oy = index / layout.Columns * TileHeight;
        for (int y = -1; y <= TileHeight; y++) for (int x = -1; x <= TileWidth; x++)
            halo[(y + 1) * 34 + x + 1] = world[((oy + y + layout.Height) % layout.Height) * layout.Width + (ox + x + layout.Width) % layout.Width];
        return halo;
    }
    public static byte[] BuildBoundary(ReadOnlySpan<byte> world, LifeLayout layout, int index)
    {
        byte[] halo = BuildHalo(world, layout, index), boundary = new byte[116];
        halo.AsSpan(0, 34).CopyTo(boundary);
        for (int y = 0; y < 24; y++)
        { boundary[34 + y * 2] = halo[(y + 1) * 34]; boundary[35 + y * 2] = halo[(y + 1) * 34 + 33]; }
        halo.AsSpan(850, 34).CopyTo(boundary.AsSpan(82));
        return boundary;
    }
    public static byte[] ReferenceStep(ReadOnlySpan<byte> cells, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height != cells.Length) throw new ArgumentException("Invalid world dimensions.");
        byte[] next = new byte[cells.Length];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int neighbors = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                if (dx != 0 || dy != 0) neighbors += cells[((y + dy + height) % height) * width + (x + dx + width) % width];
            next[y * width + x] = (byte)(neighbors == 3 || neighbors == 2 && cells[y * width + x] == 1 ? 1 : 0);
        }
        return next;
    }
    public static byte[] CreateSeed(LifeSettings settings)
    {
        settings.Validate(); var layout = settings.Layout; var random = new Random(settings.Seed);
        byte[] cells = new byte[layout.Width * layout.Height];
        for (int i = 0; i < cells.Length; i++) cells[i] = (byte)(random.Next(100) < 22 ? 1 : 0);
        return cells;
    }
}
