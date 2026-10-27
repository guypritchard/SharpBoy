using System.Buffers.Binary;
using System.Diagnostics;
using GB.Emulator.Hosting;

namespace GB.Experiments.NetworkCartridge;

public sealed record FractalRow(byte NodeId, int TileX, int TileY, int Row, byte[] Iterations, int Work);
public sealed record FractalTile(byte NodeId, int TileX, int TileY, byte[] Iterations, long Cycles, int Work);
public sealed record FractalClusterResult(int Width, int Height, int MaximumIterations, byte[] Iterations,
    IReadOnlyList<FractalTile> Tiles, double ElapsedSeconds);

public sealed record FractalLayout(int Workers, int Columns, int Rows)
{
    public int Width => Columns * FractalCluster.TileWidth;
    public int Height => Rows * FractalCluster.TileHeight;
    public static FractalLayout ForGrid(int columns, int rows)
    {
        if (columns is < 1 or > 64 || rows is < 1 or > 64 || columns * rows > 64)
            throw new ArgumentOutOfRangeException(nameof(columns), "Use positive grid dimensions with at most 64 Game Boys.");
        return new(columns * rows, columns, rows);
    }
    public static FractalLayout ForWorkers(int workers)
    {
        if (workers is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(workers), "Choose 1–64 Game Boys.");
        int rows = (int)Math.Sqrt(workers);
        while (workers % rows != 0) rows--;
        return ForGrid(workers / rows, rows);
    }
}

public sealed record FractalSettings(int Columns = 3, int Rows = 3, int Iterations = 24, int RowDelayMs = 90)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public FractalLayout Layout => FractalLayout.ForGrid(Columns, Rows);
    public void Validate()
    {
        _ = Layout;
        if (Iterations is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(Iterations), "Use 1–64 iterations.");
        if (RowDelayMs is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(RowDelayMs), "Use a row delay of 0–1000 ms.");
    }
    public static FractalSettings Load(string path)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<FractalSettings>(File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("The configuration must be a JSON object.");
        settings.Validate(); return settings;
    }
    public void Save(string path)
    {
        Validate(); File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(this,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Native SM83 tile renderers with one independent localhost TCP connection per worker.</summary>
public static class FractalCluster
{
    public const int TileWidth = 32, TileHeight = 24, Width = 96, Height = 72, MaximumIterations = 24;

    public static async Task<FractalClusterResult> RunAsync(Action<FractalRow>? onRow = null,
        Action<byte, VideoFrame>? onFrame = null, CancellationToken token = default, TimeSpan? rowDelay = null, int workers = 9,
        FractalSettings? configuration = null, Action<NativeNodeSnapshot>? onSnapshot = null)
    {
        if (rowDelay.HasValue && (rowDelay.Value < TimeSpan.Zero || rowDelay.Value > TimeSpan.FromSeconds(1)))
            throw new ArgumentOutOfRangeException(nameof(rowDelay));
        var defaultLayout = configuration == null ? FractalLayout.ForWorkers(workers) : configuration.Layout;
        var settings = configuration ?? new(defaultLayout.Columns, defaultLayout.Rows, MaximumIterations, (int)rowDelay.GetValueOrDefault().TotalMilliseconds);
        settings.Validate(); var layout = settings.Layout; workers = layout.Workers;
        rowDelay = TimeSpan.FromMilliseconds(settings.RowDelayMs);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(3));
        var stopwatch = Stopwatch.StartNew();
        Task<FractalTile>[] tasks = Enumerable.Range(0, workers).Select(index => Task.Run(async () =>
        {
            byte id = (byte)(index + 1);
            int tileX = index % layout.Columns, tileY = index / layout.Columns;
            using var listener = new LocalRecordListener(LocalNetworkTransport.Tcp);
            Task<NativeNodeResult> node = NativeNetworkNode.RunAsync("Fractal", id, LocalNetworkTransport.Tcp,
                listener.Port, lifetime.Token, onFrame == null ? null : frame => onFrame(id, frame), TimeSpan.FromMinutes(3), new byte[] { (byte)settings.Iterations }, onSnapshot);
            try
            {
                byte[] hello = await listener.ReceiveAsync(16, lifetime.Token);
                if (!hello.AsSpan(0, 4).SequenceEqual("GBW1"u8) || hello[4] != id || hello[5] != 0)
                    throw new InvalidDataException($"Node {id} sent an invalid HELLO.");
                byte[] job = new byte[16]; "GBW1"u8.CopyTo(job); job[4] = id; job[5] = 4;
                BinaryPrimitives.WriteUInt16LittleEndian(job.AsSpan(6), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(job.AsSpan(8), (ushort)tileX);
                BinaryPrimitives.WriteUInt16LittleEndian(job.AsSpan(10), (ushort)tileY);
                BinaryPrimitives.WriteUInt16LittleEndian(job.AsSpan(12), (ushort)layout.Columns);
                BinaryPrimitives.WriteUInt16LittleEndian(job.AsSpan(14), (ushort)layout.Rows);
                await listener.SendAsync(job, 16, lifetime.Token);
                byte[] pixels = new byte[TileWidth * TileHeight];
                int work = 0;
                for (int row = 0; row < TileHeight; row++)
                {
                    byte[] record = await listener.ReceiveAsync(48, lifetime.Token);
                    if (!record.AsSpan(0, 4).SequenceEqual("GBF1"u8) || record[4] != id || record[5] != row ||
                        record[6] != tileX || record[7] != tileY) throw new InvalidDataException($"Node {id} sent an invalid row.");
                    byte[] iterations = record.AsSpan(16, TileWidth).ToArray();
                    int rowWork = 0;
                    for (int column = 0; column < TileWidth; column++)
                    {
                        int expected = ReferencePixel(tileX * TileWidth + column, tileY * TileHeight + row, layout, settings.Iterations);
                        if (iterations[column] != expected) throw new InvalidDataException($"Node {id}, pixel {column}/{row} failed independent validation.");
                        rowWork += iterations[column];
                    }
                    if (BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(8)) != rowWork)
                        throw new InvalidDataException("Row work counter does not match its pixels.");
                    iterations.CopyTo(pixels, row * TileWidth); work += rowWork;
                    onRow?.Invoke(new(id, tileX, tileY, row, iterations, rowWork));
                    if (rowDelay.GetValueOrDefault() > TimeSpan.Zero) await Task.Delay(rowDelay.GetValueOrDefault(), lifetime.Token);
                    await listener.SendAsync(record.AsSpan(0, 16).ToArray(), 16, lifetime.Token);
                }
                NativeNodeResult result = await node;
                if (result.Result != 1) throw new IOException($"Fractal node {id} failed.");
                return new FractalTile(id, tileX, tileY, pixels, result.Cycles, work);
            }
            catch { lifetime.Cancel(); throw; }
            finally { try { await node; } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { } }
        }, lifetime.Token)).ToArray();
        FractalTile[] tiles = await Task.WhenAll(tasks);
        byte[] combined = new byte[layout.Width * layout.Height];
        foreach (FractalTile tile in tiles)
            for (int row = 0; row < TileHeight; row++)
                tile.Iterations.AsSpan(row * TileWidth, TileWidth).CopyTo(combined.AsSpan((tile.TileY * TileHeight + row) * layout.Width + tile.TileX * TileWidth));
        return new(layout.Width, layout.Height, settings.Iterations, combined, tiles.OrderBy(t => t.NodeId).ToArray(), stopwatch.Elapsed.TotalSeconds);
    }

    // Independent host oracle: multiply/divide directly instead of the guest's square table and subtraction scaler.
    public static byte ReferencePixel(int column, int row, int workers = 9)
        => ReferencePixel(column, row, FractalLayout.ForWorkers(workers), MaximumIterations);
    public static byte ReferencePixel(int column, int row, FractalSettings settings)
    { settings.Validate(); return ReferencePixel(column, row, settings.Layout, settings.Iterations); }
    private static byte ReferencePixel(int column, int row, FractalLayout layout, int limit)
    {
        if (column < 0 || column >= layout.Width || row < 0 || row >= layout.Height) throw new ArgumentOutOfRangeException(nameof(column));
        int real = column * 3 / layout.Columns - 72, imaginary = row * 3 / layout.Rows - 36, x = 0, y = 0;
        byte iteration = 0;
        while (iteration < limit)
        {
            if (x < -64 || x > 64 || y < -64 || y > 64 || x * x + y * y > 4096) break;
            int nextY = 2 * x * y / 32 + imaginary;
            x = (x * x - y * y) / 32 + real;
            y = nextY; iteration++;
        }
        return iteration;
    }
}
