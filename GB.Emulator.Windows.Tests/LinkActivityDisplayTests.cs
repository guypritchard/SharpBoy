using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using GB.Emulator.Windows.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class LinkActivityDisplayTests
{
    [TestMethod]
    public void HistoryMeasuresReadWriteRatesAndKeepsIdleAndConnectionChanges() => Sta(() =>
    {
        var display = new LinkActivityDisplay();
        display.Update(new(false, false, 0, 0, 0));
        Assert.AreEqual("OFFLINE", display.ConnectionText);
        display.Update(new(true, false, 0, 0, .25));
        Assert.AreEqual("WAITING", display.ConnectionText);
        display.Update(new(true, true, 16, 8, .5));
        Assert.AreEqual("CONNECTED", display.ConnectionText);
        Assert.AreEqual(64d, display.History[^1].ReadPerSecond);
        Assert.AreEqual(32d, display.History[^1].WrittenPerSecond);
        display.Update(new(true, true, 16, 8, .75));
        Assert.AreEqual(0d, display.History[^1].ReadPerSecond, "An idle link must return to a flat trace.");
        display.Update(new(false, false, 16, 8, 1));
        Assert.IsFalse(display.History[^1].Connected);
        Assert.IsTrue(display.History[^2].Connected, "Disconnecting must preserve the preceding connection history.");
        for (int i = 5; i <= 200; i++) display.Update(new(false, false, 16, 8, i * .25));
        Assert.HasCount(LinkActivityDisplay.HistoryLength, display.History);
        Assert.IsTrue(display.History.All(s => !s.Online && s.ReadPerSecond == 0 && s.WrittenPerSecond == 0));
    });

    [TestMethod]
    public void ReconnectAndClockResetDoNotInventTraffic() => Sta(() =>
    {
        var display = new LinkActivityDisplay(); Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        display.Update(new(true, true, 100, 100, 0, first));
        display.Update(new(true, true, 110, 110, .25, first));
        Assert.AreEqual(40d, display.History[^1].ReadPerSecond);
        display.Update(new(true, true, 2, 1, .5, second));
        Assert.AreEqual(0d, display.History[^1].ReadPerSecond);
        display.Update(new(true, true, 4, 3, .75, second));
        Assert.AreEqual(8d, display.History[^1].ReadPerSecond);
        display.Update(new(false, false, 0, 0, 0));
        display.Update(new(false, false, 0, 0, 10000));
        Assert.HasCount(LinkActivityDisplay.HistoryLength, display.History);
        Assert.IsTrue(display.History.All(s => s.ReadPerSecond == 0 && s.WrittenPerSecond == 0));
    });

    [TestMethod]
    public void PixelLcdRendersMeasuredTrafficInsideTheDmgStyleInstrument() => Sta(() =>
    {
        var model = new LinkCableModel();
        string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/link-monitor"));
        Directory.CreateDirectory(folder);
        model.SetStatus(new(false, false, 0, 0, 0)); Save(model.Activity.Image, Path.Combine(folder, "lcd-offline.png"));
        long read = 0, written = 0;
        for (int i = 1; i <= 144; i++)
        {
            // Deterministic rendering fixture only. Production samples the actual socket peer.
            if (i > 30) { read += i % 24 < 15 ? 12 : 2; written += i % 18 < 7 ? 8 : 1; }
            model.SetStatus(new(i > 12, i > 24, read, written, i * .25));
        }
        Save(model.Activity.Image, Path.Combine(folder, "lcd-connected.png"));
        Assert.AreEqual(160, model.Activity.Image.PixelWidth); Assert.AreEqual(144, model.Activity.Image.PixelHeight);
        var pixels = new byte[160 * 144]; model.Activity.Image.CopyPixels(pixels, 160, 0);
        Assert.IsTrue(pixels.Distinct().Order().SequenceEqual(new byte[] { 0, 1, 2, 3 }), "Use the four LCD shades without antialiasing the pixels.");
        var view = new Viewport3D { Width = 700, Height = 650,
            Camera = new OrthographicCamera(new(114, -10, 180), new(0,0,-1), new(0,1,0), 102) };
        var lights = new Model3DGroup(); lights.Children.Add(new AmbientLight(System.Windows.Media.Color.FromRgb(100,100,105)));
        lights.Children.Add(new DirectionalLight(System.Windows.Media.Color.FromRgb(185,180,165), new(-1,-1,-2)));
        view.Children.Add(new ModelVisual3D { Content = lights }); view.Children.Add(new ModelVisual3D { Content = model.HubAssembly });
        // The desktop translates this assembly; keep the fixture in its authored coordinates.
        model.HubAssembly.Transform = Transform3D.Identity;
        view.Measure(new System.Windows.Size(700,650)); view.Arrange(new Rect(0,0,700,650)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(700,650,96,96,PixelFormats.Pbgra32); bitmap.Render(view);
        Save(bitmap, Path.Combine(folder, "instrument-connected.png"));
    });

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void Sta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { check(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
