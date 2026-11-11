using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GB.Emulator.Link;
using GB.Emulator.Windows.Link;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class TetrisOpponentTests
{
    [TestMethod]
    public void OpponentUsesAnOrdinaryLobbyConnectionAndOnlyAcceptsItsOwnersInvitation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            TetrisOpponentWindow? opponent = null;
            async Task Until(Func<bool> condition)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
                Assert.IsTrue(condition(), "Opponent lobby operation timed out.");
            }
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    byte[] rom = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LINK-LOUNGE.gb"));
                    string hash = Convert.ToHexString(SHA256.HashData(rom));
                    await using var server = new LinkLobbyServer(port: 0);
                    await using var owner = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Owner", "LINK LOUNGE", hash);
                    await using var stranger = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Stranger", "LINK LOUNGE", hash);
                    opponent = new(rom, owner.State!.Self); opponent.Show();
                    await opponent.ConnectAsync("127.0.0.1", server.Port, false, null);
                    await Until(() => owner.State!.Players.Length == 3);
                    var bot = owner.State!.Players.Single(p => p.Name == "Tetris AI");
                    stranger.Invite(bot.Id);
                    await Until(() => owner.State!.Players.Single(p => p.Id == bot.Id).InviteFrom == stranger.State!.Self);
                    await Task.Delay(200);
                    Assert.AreEqual(Guid.Empty, stranger.State!.Session);
                    stranger.Unlink();
                    await Until(() => owner.State!.Players.Single(p => p.Id == bot.Id).InviteFrom == Guid.Empty);
                    owner.Invite(bot.Id);
                    await Until(() => owner.State!.Session != Guid.Empty);
                    opponent.UpdateLayout();
                    var image = new RenderTargetBitmap((int)opponent.ActualWidth, (int)opponent.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    image.Render((Visual)opponent.Content);
                    string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/link-lobby/ai-opponent.png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                    using (var file = File.Create(path)) png.Save(file);
                    opponent.Close(); await opponent.Shutdown;
                    await Until(() => owner.State!.Session == Guid.Empty);
                }
                catch (Exception ex) { failure = ex; }
                finally { opponent?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "Opponent window test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
