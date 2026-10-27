using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GB.Emulator.Hosting;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class LifeClusterTests
{
    private static bool ProcessExists(int id)
    { try { using var process = System.Diagnostics.Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; } }
    [TestMethod]
    [DataRow(2, 2, 1024, 0)]
    [DataRow(2, 2, 1024, 2)]
    [DataRow(4, 4, 1024, 0)]
    [DataRow(1, 1, 7, 0)]
    public async Task NativeTerritoriesEvolveAcrossSeamsAndDrawTheirReturnedCells(int columns, int rows, int fragmentSize, int processes)
    {
        var settings = new LifeSettings(columns, rows, 4, 0, WorkerProcesses: processes);
        var layout = settings.Layout;
        var initial = new byte[layout.Width * layout.Height];
        // A glider straddles four territories; another straddles the toroidal world's corner.
        void Glider(int ox, int oy)
        {
            foreach (var (x, y) in new[] { (1, 0), (2, 1), (0, 2), (1, 2), (2, 2) })
                initial[((oy + y) % layout.Height) * layout.Width + (ox + x) % layout.Width] = 1;
        }
        Glider(30, 22); if (columns > 1) Glider(layout.Width - 2, layout.Height - 2);
        var ticks = new ConcurrentQueue<LifeGeneration>();
        var frames = new ConcurrentDictionary<ushort, VideoFrame>();
        var snapshots = new ConcurrentDictionary<ushort, NativeNodeSnapshot>();
        var result = await LifeCluster.RunAsync(settings, ticks.Enqueue, (id, frame) => frames[id] = frame,
            s => snapshots[s.NodeId] = s, initialWorld: initial, fragmentSize: fragmentSize);
        Assert.HasCount(5, ticks);
        var expected = initial;
        foreach (var tick in ticks)
        {
            CollectionAssert.AreEqual(expected, tick.Cells.ToArray());
            Assert.AreEqual(expected.Sum(v => (int)v), tick.Alive);
            expected = LifeCluster.ReferenceStep(expected, layout.Width, layout.Height);
        }
        // Four generations translate a glider diagonally by one cell, independent of the oracle.
        var translated = new byte[initial.Length];
        for (int y = 0; y < layout.Height; y++) for (int x = 0; x < layout.Width; x++)
            translated[((y + 1) % layout.Height) * layout.Width + (x + 1) % layout.Width] = initial[y * layout.Width + x];
        CollectionAssert.AreEqual(translated, result.Final.Cells.ToArray());
        Assert.HasCount(layout.Workers, result.Nodes);
        Assert.AreEqual(processes, result.Metrics!.WorkerProcesses);
        foreach (int pid in result.Metrics.WorkerProcessIds) Assert.IsFalse(ProcessExists(pid), "Completed worker processes must be reaped.");
        for (int index = 0; index < layout.Workers; index++)
        {
            ushort id = (ushort)(index + 1);
            Assert.AreEqual((byte)1, result.Nodes[index].Result);
            Assert.AreEqual("Completed", snapshots[id].State);
            Assert.AreEqual(16L + 4 * 784, snapshots[id].Tcp.SentBytes);
            Assert.AreEqual(900L + 3 * 132 + 16, snapshots[id].Tcp.ReceivedBytes);
            var lcd = frames[id].Pixels;
            for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++)
            {
                byte alive = result.Final.Cells.Span[(index / columns * 24 + y) * layout.Width + index % columns * 32 + x];
                Assert.AreEqual(alive == 1 ? (byte)3 : (byte)0, lcd[(24 + y * 4) * 160 + 16 + x * 4]);
            }
        }
    }
    [TestMethod]
    public async Task GroupedWorkersPreserveIdsBeyondOneByteAndReturnTheirOwnMailboxHeaders()
    {
        var snapshots = new ConcurrentDictionary<ushort, NativeNodeSnapshot>();
        var result = await LifeCluster.RunAsync(new(17, 16, 2, 0, WorkerProcesses: 2), onSnapshot: s => snapshots[s.NodeId] = s);
        Assert.HasCount(272, result.Nodes); Assert.HasCount(272, snapshots);
        for (int i = 0; i < result.Nodes.Count; i++)
        {
            var mailbox = result.Nodes[i].Mailbox;
            Assert.AreEqual(i + 1, mailbox[0x84] | mailbox[0x85] << 8);
            Assert.AreEqual((byte)3, mailbox[0x8E]);
        }
        Assert.AreEqual((ushort)256, snapshots[256].NodeId); Assert.AreEqual((ushort)272, snapshots[272].NodeId);
        foreach (int id in result.Metrics!.WorkerProcessIds) Assert.IsFalse(ProcessExists(id));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task CancellationStopsEveryNativeWorkerAtABarrier(int processes)
    {
        using var stop = new CancellationTokenSource();
        var snapshots = new ConcurrentDictionary<ushort, NativeNodeSnapshot>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await LifeCluster.RunAsync(new(2, 2, 100, 0, WorkerProcesses: processes),
            onGeneration: tick => { if (tick.Generation == 1) stop.Cancel(); },
            onSnapshot: s => snapshots[s.NodeId] = s, token: stop.Token));
        Assert.HasCount(4, snapshots);
        Assert.IsTrue(snapshots.Values.All(s => s.State == "Stopped"));
    }
    [TestMethod]
    public void SettingsAndIndependentConwayRulesAreBounded()
    {
        Assert.AreEqual(new LifeSettings(2, 3, 7, 0, 5), LifeSettings.Parse(["--columns", "2", "--rows", "3", "--generations", "7", "--tick-delay-ms", "0", "--seed", "5"]));
        foreach (var bad in new[] { new LifeSettings(41, 25), new(1, 1, 0), new(1, 1, 1001), new(1, 1, 10, -1) })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(bad.Validate);
        Assert.AreEqual(1000, new LifeSettings(40, 25).Layout.Workers);
        Assert.AreEqual(16, new LifeSettings(40, 25).ProcessCount);
        Assert.ThrowsExactly<ArgumentException>(() => LifeSettings.Parse(["--unknown", "1"]));
        var blinker = new byte[25]; blinker[11] = blinker[12] = blinker[13] = 1;
        var vertical = LifeCluster.ReferenceStep(blinker, 5, 5);
        CollectionAssert.AreEqual(new byte[] { 7, 12, 17 }, vertical.Select((v, i) => (v, i)).Where(p => p.v == 1).Select(p => (byte)p.i).ToArray());
        CollectionAssert.AreEqual(blinker, LifeCluster.ReferenceStep(vertical, 5, 5));
    }
}
