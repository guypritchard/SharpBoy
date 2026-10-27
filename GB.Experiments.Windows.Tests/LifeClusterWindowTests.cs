using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Threading;
using GB.Experiments.Windows.Labs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Experiments.Windows.Tests;

[TestClass]
public class LifeClusterWindowTests
{
    [TestMethod]
    public void ThousandNodeLayoutAllocatesOnlyTheVisiblePageBeforeStarting()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new LifeClusterWindow(["--columns", "40", "--rows", "25", "--generations", "3"]);
                Assert.AreEqual(1000, window.Configuration.Layout.Workers);
                Assert.AreEqual(16, window.AllocatedLcds);
                Assert.IsNull(window.RunTask);
                window.ChangePage(62); Assert.AreEqual(8, window.AllocatedLcds);
                window.ChangePage(0); Assert.AreEqual(16, window.AllocatedLcds);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    [DataRow(2, 0)]
    [DataRow(4, 2)]
    [DataRow(5, 2)]
    public void LivingWorldCanCancelRestartInspectAndExport(int columns, int processes)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var window = new LifeClusterWindow(["--columns", columns.ToString(), "--rows", columns.ToString(), "--generations", "4", "--tick-delay-ms", "0", "--processes", processes.ToString()]);
            async Task CheckAsync()
            {
                var original = window.RunTask; Assert.IsNotNull(original);
                window.StartRun(); Assert.AreSame(original, window.RunTask);
                window.StopRun(); await original; Assert.IsNull(window.Result);
                var inspector = window.OpenNodeDetails(0);
                window.StartRun(); Assert.IsNull(inspector.Snapshot);
                await window.RunTask!;
                Assert.IsNotNull(window.Result);
                Assert.AreEqual(4, window.Result.Final.Generation);
                Assert.HasCount(columns * columns, window.Result.Nodes);
                Assert.AreSame(inspector, window.OpenNodeDetails(0));
                Assert.AreEqual("Completed", inspector.Snapshot!.State);
                Assert.IsLessThanOrEqualTo(16, window.AllocatedLcds);
                if (columns == 5)
                {
                    window.ChangePage(1); Assert.AreEqual(1, window.ActivePage);
                    Assert.IsLessThanOrEqualTo(17, window.AllocatedLcds);
                    var last = window.OpenNodeDetails(24);
                    Assert.AreEqual((ushort)25, last.Snapshot!.NodeId);
                    Assert.AreEqual("Completed", last.Snapshot.State);
                    Assert.AreEqual(160, last.Screen.PixelWidth);
                    var lcd = new int[160 * 144]; last.Screen.CopyPixels(lcd, 160 * 4, 0);
                    for (int y = 0; y < 24; y++) for (int x = 0; x < 32; x++)
                    {
                        byte cell = window.Result.Final.Cells.Span[(4 * 24 + y) * window.Result.Final.Width + 4 * 32 + x];
                        Assert.AreEqual(unchecked((int)(cell == 1 ? 0xFF1E3026u : 0xFFADB975u)), lcd[(24 + y * 4) * 160 + 16 + x * 4]);
                    }
                    last.Close(); window.ChangePage(0);
                }
                string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../artifacts/life-cluster/tests/{columns}"));
                Directory.CreateDirectory(folder);
                window.ExportResults(Path.Combine(folder, "world.json"));
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "world.json")));
                Assert.AreEqual(columns * columns * 768, json.RootElement.GetProperty("Cells").GetArrayLength());
                Assert.AreEqual(columns * columns, json.RootElement.GetProperty("Nodes").GetArrayLength());
                Assert.HasCount(6, File.ReadAllLines(Path.Combine(folder, "world.generations.csv")));
                window.Capture(Path.Combine(folder, "dashboard.png")); inspector.Capture(Path.Combine(folder, "inspector.png"));
                window.Close(); Assert.IsFalse(inspector.IsVisible);
            }
            window.Loaded += async (_, _) =>
            {
                try { await CheckAsync(); } catch (Exception ex) { failure = ex; }
                finally { window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            };
            try { window.Show(); Dispatcher.Run(); } catch (Exception ex) { failure = ex; dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(120)), "Life dashboard timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
