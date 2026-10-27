using System.Drawing;
using System.Runtime.ExceptionServices;
using GB.Experiments.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Experiments.Windows.Tests;

[TestClass]
public class ExperimentLauncherTests
{
    [TestMethod]
    public void LaunchOptionsRouteLabsAndRejectPlayerModes()
    {
        Assert.AreEqual(ExperimentMode.Launcher, ExperimentLaunch.Parse([]).Mode);
        var life = ExperimentLaunch.Parse(["--life", "--columns", "40", "--rows", "25"]);
        Assert.AreEqual(ExperimentMode.Life, life.Mode);
        CollectionAssert.AreEqual(new[] { "--columns", "40", "--rows", "25" }, life.Arguments);
        Assert.AreEqual(ExperimentMode.Fractal, ExperimentLaunch.Parse(["--cluster"]).Mode);
        Assert.AreEqual(ExperimentMode.Dhcp, ExperimentLaunch.Parse(["--dhcp"]).Mode);
        Assert.AreEqual("Realtek adapter", ExperimentLaunch.Parse(["--dhcp", "--adapter", "Realtek adapter"]).Adapter);
        Assert.ThrowsExactly<ArgumentException>(() => ExperimentLaunch.Parse(["--desktop"]));
        Assert.ThrowsExactly<ArgumentException>(() => ExperimentLaunch.Parse(["ROM.gb"]));
        Assert.ThrowsExactly<ArgumentException>(() => ExperimentLaunch.Parse(["--dhcp", "--adapter"]));
        Assert.IsFalse(typeof(ExperimentLaunch).Assembly.GetReferencedAssemblies().Any(a => a.Name == "GB.Emulator.Windows"));
    }

    [TestMethod]
    public void LauncherPresentsTheThreeExperiments()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new ExperimentLauncherForm();
                var buttons = ((TableLayoutPanel)form.Controls[0]).Controls.OfType<Button>().ToArray();
                Assert.HasCount(3, buttons);
                foreach (var experiment in ExperimentLauncherForm.Experiments)
                    Assert.IsTrue(buttons.Any(button => button.Text.StartsWith(experiment.Title, StringComparison.Ordinal)));
                form.Show(); Application.DoEvents();
                string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/experiments-windows"));
                Directory.CreateDirectory(folder);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(folder, "launcher.png"));
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
