using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Text;
using GB.Experiments.Windows;
using GB.Experiments.NetworkCartridge;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Experiments.Windows.Tests;

[TestClass]
public class DhcpWindowTests
{
    [TestMethod]
    public void LiveWindowShowsAcquiringAndBoundGameBoyScreens()
    {
        RunWindow(async form =>
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            string ReadStatus() => form.Session == null ? "" : Encoding.ASCII.GetString(form.Session.Host.Inspection.ReadMemory(0x9881, 18).Bytes).TrimEnd();
            while (ReadStatus() != "DISCOVER" && deadline.Elapsed.TotalSeconds < 6) await Task.Delay(15);
            Assert.AreEqual("DISCOVER", ReadStatus()); await Task.Delay(40); Save(form, "dhcp-acquiring.png");
            while ((form.Session?.IsComplete != true || ReadStatus() != "IP ACQUIRED") && deadline.Elapsed.TotalSeconds < 10) await Task.Delay(20);
            Assert.IsTrue(form.Session!.CaptureResult().Acquired);
            await Task.Delay(60); Save(form, "dhcp-bound.png");
        });
    }

    [TestMethod]
    public void ClosingDuringAcquisitionCompletesNetworkCleanup()
    {
        RunWindow(async form =>
        {
            while (form.Session?.HasPendingOperation != true) await Task.Delay(10);
            form.Close();
        });
    }
    private static void Save(Form form, string file)
    {
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/windows-player"));
        Directory.CreateDirectory(directory);
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(directory, file));
    }
    private static void RunWindow(Func<DhcpExperimentForm, Task> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
                using var form = new DhcpExperimentForm();
                form.Shown += async (_, _) =>
                { try { await check(form); } catch (Exception ex) { failure = ex; } finally { form.Close(); } };
                Application.Run(form);
                Assert.IsTrue(form.Cleanup.IsCompletedSuccessfully);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "DHCP window timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
