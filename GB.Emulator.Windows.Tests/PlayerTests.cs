using System.Runtime.ExceptionServices;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Windows;
using GB.Emulator.Presentation.WinForms;
using GB.Emulator.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace GB.Emulator.Windows.Tests;

[TestClass]
[DoNotParallelize] // UI tests share the process message loop and desktop.
public class PlayerTests
{
    [TestMethod]
    public void RegularPlayerDoesNotReferenceTheExperimentAssembly()
    {
        Assert.IsFalse(typeof(PlayerForm).Assembly.GetReferencedAssemblies().Any(a => a.Name?.StartsWith("GB.Experiments", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void PacingBoundsCatchUpAndDiscardsPausedTime()
    {
        var pacer = new EmulationPacer();
        Assert.AreEqual(EmulationPacer.CyclesPerFrame * 2, pacer.AddElapsed(TimeSpan.FromSeconds(30)));
        pacer.Consume(EmulationPacer.CyclesPerFrame * 2);
        Assert.AreEqual(0, pacer.AddElapsed(TimeSpan.Zero));
        Assert.AreEqual(41_943, pacer.AddElapsed(TimeSpan.FromMilliseconds(10)));
        pacer.Reset();
        Assert.AreEqual(0, pacer.AddElapsed(TimeSpan.Zero));
    }

    [TestMethod]
    public void PacingCarriesInstructionOvershootIntoTheNextTick()
    {
        var pacer = new EmulationPacer();
        int first = pacer.AddElapsed(TimeSpan.FromMilliseconds(1));
        pacer.Consume(first + 4);
        Assert.AreEqual(0, pacer.AddElapsed(TimeSpan.Zero));
        Assert.AreEqual(4_190, pacer.AddElapsed(TimeSpan.FromMilliseconds(1)));
    }

    [TestMethod]
    public void ViewportCentersIntegerPixelsAndPreservesAspectWhenFitting()
    {
        Assert.AreEqual(new Rectangle(100, 40, 800, 720), ScreenControl.GetViewport(new Size(1000, 800), true));
        Assert.AreEqual(new Rectangle(56, 0, 888, 800), ScreenControl.GetViewport(new Size(1000, 800), false));
        Assert.AreEqual(new Rectangle(10, 0, 80, 72), ScreenControl.GetViewport(new Size(100, 72), true));
        Assert.AreEqual(Rectangle.Empty, ScreenControl.GetViewport(Size.Empty, true));
    }

    [TestMethod]
    public void KeyboardMapsAllButtonsWithoutConsumingPlayerShortcuts()
    {
        Keys[] keys = [Keys.Right, Keys.Left, Keys.Up, Keys.Down, Keys.Z, Keys.X, Keys.Space, Keys.Enter];
        var mapped = new HashSet<GameBoyButton>();
        foreach (Keys key in keys)
        {
            Assert.IsTrue(PlayerKeyboardInput.TryMap(key, out GameBoyButton button));
            Assert.IsTrue(mapped.Add(button));
        }
        Assert.IsFalse(PlayerKeyboardInput.TryMap(Keys.F11, out _));
        Assert.IsFalse(PlayerKeyboardInput.TryMap(Keys.P, out _));
    }

    [TestMethod]
    public void PlayerLoadsSharedCorePausesAndResetsStartup()
    {
        WithWindow(async player =>
        {
            await player.LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"));
            player.SetPaused(true);
            Assert.IsNotNull(player.Host.Inspection.CaptureCartridge());
            Assert.IsTrue(player.Host.Inspection.CaptureHardware().StartingUp);
            player.Host.Debugging.Step();
            Assert.IsGreaterThan(0L, player.Host.Statistics.Cycles);
            player.Host.Input.SetButtonState(GameBoyButton.A, true);
            player.ResetRom();
            Assert.AreEqual(0L, player.Host.Statistics.Cycles);
            Assert.AreEqual(0, player.Host.Inspection.CaptureHardware().StartupFrames);
            Assert.IsFalse(player.Host.Input.IsPressed(GameBoyButton.A));
            StringAssert.Contains(player.Text, "SharpBoy");
        });
    }

    [TestMethod]
    public void FullscreenRestoresWindowAndInspectorIsOwnedAndReusable()
    {
        WithWindow(player =>
        {
            player.SetScale(3);
            Rectangle before = player.Bounds;
            player.ToggleFullscreen();
            Assert.AreEqual(FormBorderStyle.None, player.FormBorderStyle);
            player.ToggleFullscreen();
            Assert.AreEqual(FormBorderStyle.Sizable, player.FormBorderStyle);
            Assert.AreEqual(before, player.Bounds);
            player.ShowInspector();
            Assert.HasCount(1, player.OwnedForms);
            Assert.IsTrue(player.OwnedForms[0] is InspectorForm);
            player.ShowInspector();
            Assert.HasCount(1, player.OwnedForms);
            player.OwnedForms[0].Close();
            player.ShowInspector();
            Assert.HasCount(1, player.OwnedForms);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow(0x00)]
    [DataRow(0x01)]
    [DataRow(0x11)]
    public void StartupLogoRemainsCenteredInThePlayerViewport(int cartridgeType)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sharpboy-logo-{Guid.NewGuid():N}.gb");
        byte[] rom = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"));
        if (cartridgeType != 0)
        {
            Array.Resize(ref rom, 0x10000);
            rom[0x147] = (byte)cartridgeType;
            rom[0x148] = 1;
            Array.Fill(rom, (byte)58, 0x8000, 0x8000);
        }
        File.WriteAllBytes(path, rom);
        try
        {
            WithWindow(async player =>
            {
                await player.LoadRomAsync(path);
                player.SetPaused(true);
                for (int i = 0; i < 200; i++) player.Host.Debugging.Step();
                var screen = player.Controls.OfType<ScreenControl>().Single();
                screen.Present(player.Host.LatestFrame!);
                AssertLogoPosition(player, screen);
                player.ClientSize = new Size(913, 701);
                AssertLogoPosition(player, screen);
                player.WindowState = FormWindowState.Maximized;
                AssertLogoPosition(player, screen);
                player.ToggleFullscreen();
                AssertLogoPosition(player, screen);
                player.ToggleFullscreen();
                player.WindowState = FormWindowState.Normal;
                player.SetScale(4);
                using var image = new Bitmap(player.Width, player.Height);
                player.DrawToBitmap(image, new Rectangle(Point.Empty, player.Size));
                string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/windows-player"));
                Directory.CreateDirectory(directory);
                image.Save(Path.Combine(directory, "startup.png"));
            });
        }
        finally { File.Delete(path); }
    }

    private static void AssertLogoPosition(PlayerForm player, ScreenControl screen)
    {
        player.PerformLayout();
        Assert.AreEqual(player.ClientSize.Width / 2.0, screen.Left + screen.Width / 2.0,
            "The game display must be horizontally centered in the player.");
        Rectangle viewport = ScreenControl.GetViewport(screen.ClientSize, true);
        int scale = viewport.Width / Video.Width;
        using var image = new Bitmap(screen.Width, screen.Height);
        screen.DrawToBitmap(image, new Rectangle(Point.Empty, screen.Size));
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).ToArgb() == unchecked((int)0xFF081820))
                {
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        // The 96-pixel word is centered at x=80. Its registered mark adds eight pixels on the right.
        var expected = new Rectangle(viewport.X + 32 * scale, viewport.Y + 64 * scale, 104 * scale, 16 * scale);
        Assert.AreEqual(expected, Rectangle.FromLTRB(left, top, right + 1, bottom + 1),
            $"Startup logo bounds at {screen.ClientSize}.");
    }

    [TestMethod]
    public void LivePlayerRunsStartupHandlesInputAndPaintsTheDemo()
    {
        WithWindow(async player =>
        {
            player.PauseWhenInactive = false; // Test hosts need not own the foreground desktop.
            await player.LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB"));
            player.Activate();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (player.Host.Inspection.CaptureHardware().StartingUp && timeout.Elapsed.TotalSeconds < 12)
                await Task.Delay(50);
            Assert.IsFalse(player.Host.Inspection.CaptureHardware().StartingUp,
                $"Live player did not finish startup: {player.Host.Inspection.CaptureHardware().StartupFrames} frames, {player.Host.Statistics.Cycles} cycles.");
            await Task.Delay(200);
            Assert.IsGreaterThan(EmulationPacer.CyclesPerFrame * 264L, player.Host.Statistics.Cycles);
            Message key = Message.Create(player.Handle, 0x100, (IntPtr)Keys.Z, IntPtr.Zero);
            var keyboard = new PlayerKeyboardInput(player, player.Host.Input,
                () => player.Host.State == EmulationState.Running, () => Keys.None);
            Assert.IsTrue(keyboard.PreFilterMessage(ref key), $"Input rejected: state={player.Host.State}, fault={player.Host.Fault}");
            Assert.IsTrue(player.Host.Input.IsPressed(GameBoyButton.A));
            Message release = Message.Create(player.Handle, 0x101, (IntPtr)Keys.Z, IntPtr.Zero);
            Assert.IsTrue(Application.FilterMessage(ref release));
            Assert.IsFalse(player.Host.Input.IsPressed(GameBoyButton.A));
            Assert.IsTrue(keyboard.PreFilterMessage(ref key));
            player.SetPaused(true);
            Assert.IsFalse(player.Host.Input.IsPressed(GameBoyButton.A));
            long cycles = player.Host.Statistics.Cycles;
            await Task.Delay(50);
            Assert.AreEqual(cycles, player.Host.Statistics.Cycles);
            using var image = new Bitmap(player.Width, player.Height);
            player.DrawToBitmap(image, new Rectangle(Point.Empty, player.Size));
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/windows-player"));
            Directory.CreateDirectory(directory);
            image.Save(Path.Combine(directory, "player.png"));
            player.ShowInspector();
            var inspector = (InspectorForm)player.OwnedForms[0];
            await Task.Delay(100);
            var tabs = (TabControl)inspector.Controls[0];
            var cartridgeText = (TextBox)tabs.TabPages[0].Controls[0];
            StringAssert.Contains(cartridgeText.Text, "ROM file:");
            StringAssert.Contains(cartridgeText.Text, "SPRITE.GB");
            inspector.Refresh();
            using var inspectorImage = new Bitmap(inspector.Width, inspector.Height);
            inspector.DrawToBitmap(inspectorImage, new Rectangle(Point.Empty, inspector.Size));
            inspectorImage.Save(Path.Combine(directory, "inspector.png"));
        });
    }

    private static void WithWindow(Func<PlayerForm, Task> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, threadScope: true);
                using var player = new PlayerForm();
                player.Shown += async (_, _) =>
                {
                    try { await check(player); }
                    catch (Exception ex) { failure = ex; }
                    finally { player.Close(); }
                };
                Application.Run(player);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Player UI test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
