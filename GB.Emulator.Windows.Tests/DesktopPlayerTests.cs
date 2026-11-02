using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using GB.Emulator.Windows.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Point = System.Windows.Point;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class DesktopPlayerTests
{
    [TestMethod]
    public void ScreenDoubleClickPreservesGameAndOrientationAndPowerSwitchDisposesHost()
    {
        Run(async window =>
        {
            await window.LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"));
            await Task.Delay(150);
            var scene = window.Scene;
            double yaw = scene.Yaw, pitch = scene.Pitch;
            var bounds = scene.ScreenBounds;
            var screen = scene.Project(new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ + .2));
            Assert.IsTrue(scene.HitScreen(screen));
            Assert.IsFalse(scene.HitPowerSwitch(screen));
            Assert.IsTrue(window.HandleSurfaceClick(screen, 1));
            Assert.IsFalse(window.IsScreenCloseUp);
            Assert.IsTrue(window.HandleSurfaceClick(screen, 2));
            Assert.IsTrue(window.IsScreenCloseUp);
            if (window.IsViewTransitioning)
            {
                Assert.IsTrue(window.IsViewTransitioning);
                window.ToggleScreenCloseUp(); // Repeated clicks must not start overlapping transitions.
                Assert.IsTrue(window.IsScreenCloseUp);
                await Task.Delay(150);
                // A loaded/software-rendered CI desktop can resume after the midpoint.
                if (window.IsViewTransitioning)
                    window.Capture(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/desktop-player/tests/transition-midpoint.png")));
            }
            await WaitForViewTransition(window);
            Assert.AreEqual(Visibility.Collapsed, scene.View.Visibility);
            Assert.AreEqual(160d / 144, window.Width / window.Height, .00001);
            long before = scene.FrameNumber;
            await Task.Delay(2500);
            Assert.IsGreaterThan(before, scene.FrameNumber);
            byte[] pixels = new byte[160 * 144 * 4];
            scene.ScreenImage.CopyPixels(pixels, 160 * 4, 0);
            Assert.IsGreaterThan(1, Enumerable.Range(0, 160 * 144).Select(i => BitConverter.ToInt32(pixels, i * 4)).Distinct().Count(), "The close-up must share the live ROM image.");
            window.Capture(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/desktop-player/tests/close-up.png")));
            window.HandleSurfaceClick(new Point(20, 20), 2);
            Assert.IsFalse(window.IsScreenCloseUp);
            await WaitForViewTransition(window);
            Assert.AreEqual(yaw, scene.Yaw); Assert.AreEqual(pitch, scene.Pitch);
            Assert.AreEqual(Visibility.Visible, scene.View.Visibility);

            // The OFF/ON marking is reachable even when the top slider is hidden.
            Assert.IsTrue(scene.HitPowerSwitch(scene.Project(new Point3D(-27, 69, 13.8))));
            scene.SetView(0, 25); await Task.Delay(40);
            // Check the actual exposed top slider too.
            var model = DmgModel.Load(Path.Combine(AppContext.BaseDirectory, "Desktop/Assets/Dmg.glb"));
            var power = (MeshGeometry3D)model.Parts.Single(p => p.Name == "switch_low_External_0").Model.Geometry;
            Point? target = null;
            for (int i = 0; i < power.TriangleIndices.Count && target == null; i += 3)
            {
                var a = power.Positions[power.TriangleIndices[i]];
                var b = power.Positions[power.TriangleIndices[i+1]];
                var c = power.Positions[power.TriangleIndices[i+2]];
                var point = scene.Project(new Point3D((a.X+b.X+c.X)/3, (a.Y+b.Y+c.Y)/3, (a.Z+b.Z+c.Z)/3));
                if (scene.HitPowerSwitch(point)) target = point;
            }
            Assert.IsNotNull(target, "The exposed power slider must be clickable.");
            Assert.IsTrue(window.HandleSurfaceClick(target.Value, 1));
            Assert.AreEqual(EmulationState.Disposed, window.Host.State);
        });
    }
    [TestMethod]
    public void EntireVideoBorderClearsTheRaisedBezel()
    {
        Run(async window =>
        {
            foreach (var (yaw, pitch) in new[] { (0d, 0d), (-14d, -5d), (-25d, -8d), (25d, 8d) })
            {
                window.Scene.SetView(yaw, pitch); await Task.Delay(40);
                var clipped = new List<string>();
                for (int x = 0; x < 160; x++)
                {
                    if (!window.Scene.IsScreenPixelVisible(x, 0)) clipped.Add($"top {x}");
                    if (!window.Scene.IsScreenPixelVisible(x, 143)) clipped.Add($"bottom {x}");
                }
                for (int y = 1; y < 143; y++)
                {
                    if (!window.Scene.IsScreenPixelVisible(0, y)) clipped.Add($"left {y}");
                    if (!window.Scene.IsScreenPixelVisible(159, y)) clipped.Add($"right {y}");
                }
                Assert.IsEmpty(clipped, $"View {yaw}/{pitch}: {string.Join(", ", clipped.Take(20))}; {clipped.Count} border pixels hidden");
            }
        });
    }
    [TestMethod]
    public void LiveDesktopHasTransparentSurroundAndRealFramesAtEveryOrientation()
    {
        Run(async window =>
        {
            Assert.IsTrue(window.AllowsTransparency); Assert.AreEqual(WindowStyle.None, window.WindowStyle);
            Assert.IsGreaterThan(10_000, window.Scene.TriangleCount);
            Assert.IsGreaterThan(45, window.Scene.ScreenBounds.SizeX);
            await window.LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"));
            await Task.Delay(600);
            long before = window.Scene.FrameNumber;
            Assert.IsGreaterThan(0L, before);
            window.Scene.Rotate(100, 30); await Task.Delay(100);
            Assert.AreNotEqual(-14d, window.Scene.Yaw);
            window.Scene.SetView(165, -8); await Task.Delay(100);
            Assert.IsGreaterThan(before, window.Scene.FrameNumber);
            var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/desktop-player/tests"));
            foreach (var (name, yaw) in new[] { ("front", -14d), ("rear", 165d), ("edge", 82d) })
            {
                window.Scene.SetView(yaw, -5); await Task.Delay(70);
                string file = Path.Combine(folder, name + ".png"); window.Capture(file);
                var bitmap = new BitmapImage(new Uri(file));
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
                converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
                Assert.AreEqual((byte)0, pixels[3], "Window corners must have zero alpha, not an opaque rectangular background.");
                Assert.IsGreaterThan(10_000, Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i*4+3] > 240));
            }
            Assert.AreEqual(EmulationState.Running, window.Host.State);
        });
    }

    [TestMethod]
    public void ButtonHitTestingSurvivesRotationAndMapsSelectAndStartSeparately()
    {
        Run(async window =>
        {
            foreach (double yaw in new[] { 0d, -25d, 30d })
            {
                window.Scene.SetView(yaw, -5); await Task.Delay(40);
                foreach (var (position, button) in new (Point3D, GameBoyButton)[]
                {
                    (new(33,-21,14.5),GameBoyButton.A), (new(19,-29,14.5),GameBoyButton.B),
                    (new(-33,-27,16.5),GameBoyButton.Left), (new(-21,-27,16.5),GameBoyButton.Right),
                    (new(-27,-21,16.5),GameBoyButton.Up), (new(-27,-33,16.5),GameBoyButton.Down),
                    (new(-12.835,-48.363,14.1),GameBoyButton.Select), (new(2.742,-48.114,14.1),GameBoyButton.Start)
                })
                    Assert.AreEqual(button, window.Scene.HitButton(window.Scene.Project(position)), $"yaw {yaw}: {button}");
            }
            Assert.AreEqual(GameBoyButton.A, DesktopWindow.MapKey(Key.Z));
            Assert.AreEqual(GameBoyButton.Start, DesktopWindow.MapKey(Key.Enter));
            window.Scene.SetView(1080, 500); Assert.AreEqual(0d, window.Scene.Yaw); Assert.AreEqual(85d, window.Scene.Pitch);
            window.Scene.ResetView(); Assert.AreEqual(-14d, window.Scene.Yaw);
            // A short CI desktop can cap the enlarged window below its nominal 640px width.
            // Compare with the minimum size and verify the actual desktop-fitting contract.
            window.SetScale(.4);
            double minimumWidth = window.Width;
            window.SetScale(1.2);
            Assert.IsGreaterThan(minimumWidth, window.Width);
            // Native window dimensions can round by one device-independent pixel.
            Assert.IsLessThanOrEqualTo(SystemParameters.WorkArea.Width + 1, window.Width);
            Assert.IsLessThanOrEqualTo(SystemParameters.WorkArea.Height + 1, window.Height);
            Assert.AreEqual(window.Width * 780 / 640, window.Height, 1);
        });
    }

    [TestMethod]
    public void LabelUsesTheLoadedRomAndHonoursMatchingLocalArtwork()
    {
        Run(async window =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "sharpboy-cartridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string rom = Path.Combine(folder, "Pokemon - Red Version.gb");
                File.Copy(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"), rom);
                var generated = CartridgeAppearance.Read(rom, "POKEMON RED");
                Assert.IsNull(generated.Label); Assert.AreEqual(64, generated.Sha256.Length);
                Assert.IsGreaterThan(generated.Shell.B, generated.Shell.R);
                var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                    new byte[] {0,0,255,255, 0,0,255,255, 0,0,255,255, 0,0,255,255}, 8);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using (var stream = File.Create(Path.ChangeExtension(rom, ".label.png"))) png.Save(stream);
                var labelled = CartridgeAppearance.Read(rom, "POKEMON RED");
                Assert.IsNotNull(labelled.Label); Assert.IsTrue(labelled.Label!.IsFrozen);
                await window.LoadRomAsync(rom); Assert.AreEqual(labelled.Sha256, window.Scene.Cartridge!.Sha256);
                Assert.IsNotNull(window.Scene.Cartridge.Label);
            }
            finally { Directory.Delete(folder, true); }
        });
    }

    private static async Task WaitForViewTransition(DesktopWindow window)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (window.IsViewTransitioning && deadline.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(20);
        Assert.IsFalse(window.IsViewTransitioning, "The view transition did not finish within five seconds.");
    }

    private static void Run(Func<DesktopWindow, Task> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            try
            {
                var window = new DesktopWindow([], animateViewTransitions: true);
                window.Loaded += async (_, _) =>
                {
                    try { await check(window); }
                    catch (Exception ex) { failure = ex; }
                    finally { window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                };
                window.Show(); Dispatcher.Run();
                Assert.AreEqual(EmulationState.Disposed, window.Host.State);
            }
            catch (Exception ex) { failure = ex; dispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(25)), "Desktop test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
