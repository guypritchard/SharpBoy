using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GB.Emulator.AI.Tetris;
using GB.Emulator.Windows.Link;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class TetrisStyleTests
{
    [TestMethod]
    public void SlidersApplyLiveAndPresetsRefreshEveryControl()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            TetrisStyleWindow? window = null;
            try
            {
                TetrisAiSettings? applied = null;
                window = new(new(), settings => applied = settings); window.Show();
                window.Sliders["AvoidHoles"].Value = 3.25;
                Assert.IsNotNull(applied); Assert.AreEqual(3.25, applied.AvoidHoles);
                window.Sliders["ReleaseFrames"].Value = 8;
                Assert.AreEqual(8, applied.ReleaseFrames);
                window.SetSettings(TetrisAiSettings.Presets["Strategist"]);
                Assert.AreEqual(.8, window.Sliders["Lookahead"].Value);
                Assert.AreEqual(1, window.Sliders["HoldFrames"].Value);
                Assert.AreEqual(TetrisAiSettings.Presets["Strategist"], applied);
                window.UpdateLayout();
                var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render((Visual)window.Content);
                string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/link-lobby/ai-playing-style.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(path); png.Save(file);
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(15)), "AI style editor test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
