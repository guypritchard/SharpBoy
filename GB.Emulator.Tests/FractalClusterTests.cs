using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using GB.Emulator.Hosting;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class FractalClusterTests
{
    [TestMethod]
    public async Task InspectionPublishesLiveAndFinalDetachedSnapshotsAndCancellation()
    {
        var samples = new ConcurrentQueue<NativeNodeSnapshot>();
        var result = await FractalCluster.RunAsync(configuration: new(1, 1, 8, 15), onSnapshot: samples.Enqueue);
        var all = samples.ToArray();
        Assert.IsTrue(all.Any(s => s.State == "Running"));
        Assert.AreEqual("Completed", all[^1].State);
        Assert.AreEqual(result.Tiles[0].Cycles, all[^1].Cycles);
        Assert.AreEqual(1168L, all[^1].Tcp.SentBytes);
        Assert.AreEqual(400L, all[^1].Tcp.ReceivedBytes);
        for (int n = 1; n < all.Length; n++) Assert.IsGreaterThanOrEqualTo(all[n - 1].Cycles, all[n].Cycles);
        using var stop = new CancellationTokenSource();
        samples.Clear();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await FractalCluster.RunAsync(configuration: new(1, 1, 24, 100), token: stop.Token,
                onSnapshot: snapshot => { samples.Enqueue(snapshot); if (snapshot.State == "Running") stop.Cancel(); }));
        Assert.AreEqual("Stopped", samples.Last().State);
    }

    [TestMethod]
    [DataRow(2, 3, 12)]
    [DataRow(1, 1, 1)]
    [DataRow(1, 1, 64)]
    public async Task CustomGridAndIterationLimitsExecuteInTheGuest(int columns, int gridRows, int limit)
    {
        var settings = new FractalSettings(columns, gridRows, limit, 0);
        var frames = new ConcurrentDictionary<byte, VideoFrame>();
        var result = await FractalCluster.RunAsync(onFrame: (id, frame) => frames[id] = frame, configuration: settings);
        Assert.AreEqual(limit, result.MaximumIterations);
        Assert.AreEqual(settings.Layout.Width, result.Width); Assert.AreEqual(settings.Layout.Height, result.Height);
        Assert.HasCount(columns * gridRows, result.Tiles);
        foreach (var tile in result.Tiles)
        {
            ReadOnlySpan<byte> lcd = frames[tile.NodeId].Pixels;
            for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++)
            {
                byte value = tile.Iterations[y * 32 + x];
                Assert.AreEqual(FractalCluster.ReferencePixel(tile.TileX * 32 + x, tile.TileY * 24 + y, settings), value);
                Assert.AreEqual(value == limit ? (byte)3 : (byte)(value & 3), lcd[(24 + y * 4) * 160 + 16 + x * 4]);
            }
        }
    }

    [TestMethod]
    public void SettingsRoundTripValidateBoundsAndAcceptEveryWorkerCount()
    {
        for (int workers = 1; workers <= 64; workers++)
        {
            var grid = FractalLayout.ForWorkers(workers);
            Assert.AreEqual(workers, grid.Columns * grid.Rows);
        }
        foreach (var bad in new[] { new FractalSettings(0, 1), new(9, 8), new(1, 1, 0), new(1, 1, 65), new(1, 1, 24, -1), new(1, 1, 24, 1001) })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => bad.Validate());
        string file = Path.GetTempFileName();
        try
        {
            var settings = new FractalSettings(5, 4, 48, 120); settings.Save(file);
            Assert.AreEqual(settings, FractalSettings.Load(file));
            File.WriteAllText(file, "{\"Colums\": 4}");
            Assert.ThrowsExactly<JsonException>(() => FractalSettings.Load(file));
        }
        finally { File.Delete(file); }
    }
    [TestMethod]
    [DataRow(9)]
    [DataRow(16)]
    [DataRow(32)]
    public async Task NativeNodesReturnEveryPixelAndDrawTheSameValuesOnTheirOwnLcds(int workers)
    {
        var layout = FractalLayout.ForWorkers(workers);
        var frames = new ConcurrentDictionary<byte, VideoFrame>();
        var rows = new ConcurrentBag<FractalRow>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        FractalClusterResult result = await FractalCluster.RunAsync(rows.Add, (id, frame) => frames[id] = frame, deadline.Token, workers: workers);
        Assert.HasCount(workers, result.Tiles); Assert.HasCount(workers, frames);
        Assert.HasCount(workers * FractalCluster.TileHeight, rows);
        Assert.HasCount(layout.Width * layout.Height, result.Iterations);
        Assert.AreEqual(layout.Width, result.Width); Assert.AreEqual(layout.Height, result.Height);
        Assert.AreEqual(result.Iterations.Sum(b => (int)b), result.Tiles.Sum(t => t.Work));
        foreach (FractalTile tile in result.Tiles)
        {
            Assert.IsGreaterThan(0L, tile.Cycles);
            ReadOnlySpan<byte> pixels = frames[tile.NodeId].Pixels;
            for (int y = 0; y < FractalCluster.TileHeight; y++)
                for (int x = 0; x < FractalCluster.TileWidth; x++)
                {
                    byte iteration = tile.Iterations[y * FractalCluster.TileWidth + x];
                    Assert.AreEqual(FractalCluster.ReferencePixel(tile.TileX * 32 + x, tile.TileY * 24 + y, workers), iteration);
                    byte shade = iteration == 24 ? (byte)3 : (byte)(iteration & 3);
                    Assert.AreEqual(shade, pixels[(24 + y * 4) * 160 + 16 + x * 4], $"GB{tile.NodeId}: LCD {x}/{y}");
                    Assert.AreEqual(iteration, result.Iterations[(tile.TileY * 24 + y) * result.Width + tile.TileX * 32 + x]);
                }
        }
        Assert.IsGreaterThan(3, result.Iterations.Distinct().Count());
    }
}
