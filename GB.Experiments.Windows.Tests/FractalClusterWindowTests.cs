using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GB.Experiments.Windows.Labs;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Experiments.Windows.Tests;

[TestClass]
public class FractalClusterWindowTests
{
    [TestMethod]
    [DataRow(9)]
    [DataRow(16)]
    [DataRow(32)]
    [DataRow(6)]
    public void LcdDashboardCanCancelRestartCollectAndExportNativeResults(int workers)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var window = new FractalClusterWindow(workers == 6
                ? ["--columns", "2", "--rows", "3", "--iterations", "12", "--row-delay-ms", "0"]
                : ["--workers", workers.ToString()]);
            var layout = window.Configuration.Layout;
            async Task CheckAsync()
            {
                    Assert.IsNotNull(window.RunTask);
                    Assert.AreEqual(window.Configuration, window.ReadConfiguration());
                    var original = window.RunTask;
                    window.StartRun(); Assert.AreSame(original, window.RunTask, "Concurrent restarts must not create duplicate nodes.");
                    window.StopRun(); await original;
                    Assert.IsNull(window.Result);
                    window.StartRun(); Assert.AreNotSame(original, window.RunTask);
                    var inspector = window.OpenNodeDetails(0);
                    Assert.AreSame(inspector, window.OpenNodeDetails(0), "Repeated clicks should reuse the node inspector.");
                    await window.RunTask!;
                    Assert.IsNotNull(window.Result);
                    Assert.HasCount(workers, window.Result.Tiles);
                    Assert.IsTrue(window.CompletedRows.All(rows => rows == 24));
                    string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../artifacts/fractal-cluster/tests/{workers}"));
                    Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, "collected.json"); window.ExportResults(file);
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    Assert.AreEqual(layout.Width * layout.Height, json.RootElement.GetProperty("Pixels").GetArrayLength());
                    Assert.AreEqual(workers, json.RootElement.GetProperty("Nodes").GetArrayLength());
                    Assert.AreEqual(workers, json.RootElement.GetProperty("Workers").GetInt32());
                    Assert.AreEqual(window.Configuration.Iterations, json.RootElement.GetProperty("MaximumIterations").GetInt32());
                    Assert.HasCount(layout.Width * layout.Height + 1, File.ReadAllLines(Path.Combine(folder, "collected.pixels.csv")));
                    Assert.HasCount(workers + 1, File.ReadAllLines(Path.Combine(folder, "collected.nodes.csv")));
                    var png = new BitmapImage(new Uri(Path.Combine(folder, "collected.image.png")));
                    Assert.AreEqual(layout.Width, png.PixelWidth); Assert.AreEqual(layout.Height, png.PixelHeight);
                    var inspected = window.OpenNodeDetails(0);
                    Assert.AreSame(inspector, inspected);
                    Assert.IsNotNull(inspected.Snapshot);
                    Assert.AreEqual("Completed", inspected.Snapshot.State);
                    Assert.AreEqual(1, inspected.Snapshot.NodeId);
                    Assert.AreEqual(window.Result.Tiles[0].Cycles, inspected.Snapshot.Cycles);
                    Assert.IsTrue(inspected.Snapshot.NonZeroWorkRamBytes is > 0 and <= 8192);
                    Assert.IsGreaterThan(0d, inspected.Snapshot.CyclesPerSecond);
                    Assert.AreEqual(1168L, inspected.Snapshot.Tcp.SentBytes); // HELLO + 24 row records
                    Assert.AreEqual(400L, inspected.Snapshot.Tcp.ReceivedBytes); // job + 24 ACKs
                    Assert.StartsWith("127.0.0.1:", inspected.Snapshot.Tcp.LocalEndpoint!);
                    Assert.StartsWith("127.0.0.1:", inspected.Snapshot.Tcp.RemoteEndpoint);
                    Assert.AreEqual(0, inspected.Snapshot.NetworkStatus & NetworkCartridgeDevice.Connected);
                    Assert.AreEqual(NetworkError.None, inspected.Snapshot.NetworkError);
                    Assert.AreEqual(1024, inspected.Snapshot.NetworkBufferBytes);
                    inspected.Capture(Path.Combine(folder, "inspector.png"));
                    window.Capture(Path.Combine(folder, "dashboard.png"));
                    inspected.Close();
                    var reopened = window.OpenNodeDetails(0);
                    Assert.AreNotSame(inspected, reopened);
                    Assert.AreEqual("Completed", reopened.Snapshot!.State);
                    if (workers == 6)
                    {
                        window.StartRun();
                        Assert.IsNull(reopened.Snapshot, "A new run must clear the retained inspection immediately.");
                        window.StopRun(); await window.RunTask!;
                    }
                    window.Close();
                    Assert.IsFalse(reopened.IsVisible, "Closing the dashboard closes its inspectors.");
            }
            window.Loaded += async (_, _) =>
            {
                try { await CheckAsync(); }
                catch (Exception ex) { failure = ex; }
                finally { window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            };
            try { window.Show(); Dispatcher.Run(); }
            catch (Exception ex) { failure = ex; dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(120)), "Visual cluster test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
