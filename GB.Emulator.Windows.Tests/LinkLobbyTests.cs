using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Link;
using GB.Emulator.Windows.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Windows.Tests;

[TestClass]
public class LinkLobbyTests
{
    [TestMethod]
    public void HiddenLobbyRetriesUnavailableAndRestartedServerUntilExplicitlyLeft()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                GB.Emulator.Windows.Link.LinkLobbyWindow? lobby = null;
                LinkLobbyServer? server = null;
                async Task Until(Func<bool> condition)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(25);
                    Assert.IsTrue(condition(), lobby?.ConnectionStatus);
                }
                try
                {
                    var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                    reservation.Start(); int port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
                    using var host = new GB.Emulator.Hosting.EmulationHost();
                    host.Load(new GB.Emulator.Core.Cartridge { Data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LINK-LOUNGE.gb")) });
                    var monitor = new LinkCableModel(); bool online = false;
                    lobby = new(host, activity => { online = activity.Online; monitor.SetStatus(activity); });
                    lobby.Show(); lobby.Hide();
                    await lobby.JoinAsync("127.0.0.1", port, "Retry test", autoMatch: false);
                    Assert.IsNull(lobby.Client);
                    StringAssert.Contains(lobby.ConnectionStatus, "Retrying");
                    server = new LinkLobbyServer(port: port);
                    await Until(() => online);
                    Assert.AreEqual(System.Windows.Media.Color.FromRgb(255,180,48), monitor.LedColor, "A server connection alone must illuminate the lamp.");
                    Assert.AreEqual("WAITING", monitor.Activity.ConnectionText);
                    Guid original = lobby.Client!.State!.Self;
                    await server.DisposeAsync(); server = null;
                    await Until(() => !online);
                    Assert.AreEqual(System.Windows.Media.Color.FromRgb(68,39,39), monitor.LedColor);
                    server = new LinkLobbyServer(port: port);
                    await Until(() => online && lobby.Client!.State!.Self != original);
                    await lobby.LeaveAsync();
                    Assert.IsFalse(online);
                    await Task.Delay(2200);
                    Assert.IsNull(lobby.Client, "Leaving must cancel automatic reconnection.");
                    // Leaving also cancels retries after an unsuccessful initial connection.
                    await server.DisposeAsync(); server = null;
                    await lobby.JoinAsync("127.0.0.1", port, "Cancelled retry", autoMatch: false);
                    await lobby.LeaveAsync(); server = new LinkLobbyServer(port: port);
                    await Task.Delay(2200);
                    Assert.IsNull(lobby.Client);
                    var stalled = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                    stalled.Start();
                    try
                    {
                        var pending = lobby.JoinAsync("127.0.0.1", ((System.Net.IPEndPoint)stalled.LocalEndpoint).Port, "Cancel handshake");
                        using var socket = await stalled.AcceptTcpClientAsync();
                        await lobby.LeaveAsync();
                        await pending.WaitAsync(TimeSpan.FromSeconds(2));
                        Assert.IsNull(lobby.Client, "Leaving during a handshake must cancel it without retrying.");
                    }
                    finally { stalled.Stop(); }
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    if (lobby != null) { lobby.Close(); await lobby.Shutdown; }
                    if (server != null) await server.DisposeAsync();
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(40)), "Reconnect test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void ServerOpponentConnectsWithoutCreatingAnOpponentWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                GB.Emulator.Windows.Link.LinkLobbyWindow? lobby = null;
                try
                {
                    await using var server = new LinkLobbyServer(port: 0);
                    await using var bot = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Server CPU", "TETRIS", new string('0', 64), autoMatch: false);
                    server.RegisterAutomatedPlayer(bot.State!.Self);
                    using var host = new GB.Emulator.Hosting.EmulationHost();
                    byte[] rom = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LINK-LOUNGE.gb"));
                    Array.Clear(rom, 0x134, 16); System.Text.Encoding.ASCII.GetBytes("TETRIS").CopyTo(rom, 0x134);
                    host.Load(new GB.Emulator.Core.Cartridge { Data = rom });
                    lobby = new(host); lobby.Show();
                    await lobby.JoinAsync("127.0.0.1", server.Port, "Human");
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (lobby.Client?.Peer.IsConnected != true && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
                    Assert.IsTrue(lobby.Client?.Peer.IsConnected, lobby.ConnectionStatus);
                    Assert.IsNull(lobby.Opponent, "Normal matchmaking must not create another emulator on the client.");
                    Assert.AreEqual(0, lobby.OwnedWindows.Count);
                    lobby.Close(); await lobby.Shutdown; lobby = null;
                }
                catch (Exception ex) { failure = ex; }
                finally { if (lobby != null) { lobby.Close(); await lobby.Shutdown; } dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Lobby test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestMethod]
    public void NativeSdkGamesConnectInTheLobbyExchangeScoresAndKeepCableGeometryAttached()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            DesktopWindow? a = null, b = null;
            async Task Until(Func<bool> condition)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
                Assert.IsTrue(condition(), "The native link/lobby operation timed out.");
            }
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    int.TryParse(Environment.GetEnvironmentVariable("SHARPBOY_LINK_TEST_PORT"),out int externalPort);
                    await using var server = externalPort==0?new LinkLobbyServer(port:0):null;
                    int serverPort = externalPort==0?server!.Port:externalPort;
                    a = new DesktopWindow([]); b = new DesktopWindow([]); a.Show(); b.Show();
                    a.Host.Muted = b.Host.Muted = true;
                    string rom = Path.Combine(AppContext.BaseDirectory, "LINK-LOUNGE.gb");
                    await a.LoadRomAsync(rom); await b.LoadRomAsync(rom);
                    a.OpenLinkLobby(); b.OpenLinkLobby();
                    Assert.IsTrue(a.Scene.IsLinkVisible); Assert.IsGreaterThan(4000, a.Scene.Cable!.TriangleCount);
                    var model = DmgModel.Load(Path.Combine(AppContext.BaseDirectory, "Desktop/Assets/Dmg.glb"));
                    Assert.IsTrue(model.Parts.Single(p => p.Name == "dviMetal_low_External_0").Model.Bounds.Contains(LinkCableModel.PortAnchor));
                    var la = a.LinkLobby!; var lb = b.LinkLobby!;
                    await la.JoinAsync("127.0.0.1", serverPort, "Player one"); await lb.JoinAsync("127.0.0.1", serverPort, "Player two");
                    Assert.IsNotNull(la.Client,la.ConnectionStatus); Assert.IsNotNull(lb.Client,lb.ConnectionStatus);
                    await Until(() => la.Client.State!.Players.Length == 2);
                    la.Client.Invite(lb.Client.State!.Self);
                    await Until(() => lb.Client.State!.Players.Single(p => p.Id == lb.Client.State.Self).InviteFrom != Guid.Empty);
                    lb.Client.Accept(la.Client.State!.Self);
                    await Until(() => la.Client.Peer.IsConnected && lb.Client.Peer.IsConnected);
                    // Advance the synthetic boot deterministically; this is a link/UI check,
                    // independent of occluded-window rendering speed on CI machines.
                    for (int frame=0;frame<300&&(a.Host.Inspection.CaptureHardware().StartingUp||b.Host.Inspection.CaptureHardware().StartingUp);frame++)
                    {
                        if(a.Host.Inspection.CaptureHardware().StartingUp)a.Host.Advance(TimeSpan.FromSeconds(1.0/60));
                        if(b.Host.Inspection.CaptureHardware().StartingUp)b.Host.Advance(TimeSpan.FromSeconds(1.0/60));
                    }
                    Assert.IsFalse(a.Host.Inspection.CaptureHardware().StartingUp,a.Host.Inspection.CaptureHardware().ToString());
                    Assert.IsFalse(b.Host.Inspection.CaptureHardware().StartingUp,b.Host.Inspection.CaptureHardware().ToString());
                    await Task.Delay(300);
                    a.Host.SetButtonState(GameBoyButton.Start, true); b.Host.SetButtonState(GameBoyButton.Select, true);
                    await Task.Delay(300); a.Host.ReleaseAll(); b.Host.ReleaseAll();
                    await Until(() => la.Client.BytesExchanged > 3 && lb.Client.BytesExchanged > 3);
                    a.Host.SetButtonState(GameBoyButton.A, true); await Task.Delay(200); a.Host.ReleaseAll();
                    await Until(() => b.Host.Inspection.ReadMemory(0x99E9, 1)[0] == 49);
                    await Until(() => a.Scene.Cable!.Activity.History.Any(s => s.ReadPerSecond > 0 && s.WrittenPerSecond > 0));
                    Assert.AreEqual("CONNECTED", a.Scene.Cable!.Activity.ConnectionText);
                    Assert.AreEqual(System.Windows.Media.Color.FromRgb(239,48,44), a.Scene.Cable.LedColor);
                    string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/link-lobby"));
                    la.Capture(Path.Combine(folder, "lobby-paired.png"));
                    a.Capture(Path.Combine(folder, "desktop-linked-player-one.png")); b.Capture(Path.Combine(folder, "desktop-linked-player-two.png"));
                    la.DisconnectCable(); await Until(() => lb.Client.State!.Session == Guid.Empty);
                    la.Close(); lb.Close(); await la.Shutdown; await lb.Shutdown;
                    Assert.IsFalse(a.Scene.IsLinkVisible); Assert.IsFalse(b.Scene.IsLinkVisible);
                }
                catch (Exception ex) { failure = ex; }
                finally { a?.Close(); b?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(55)), "Link UI test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
