using System.Diagnostics;
using System.Security.Cryptography;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using GB.Emulator.Link;
using GB.Link.Server;

if (args.Length is not (1 or 3)) throw new ArgumentException("Usage: TetrisServerProbe <private Tetris ROM> [server port]");
byte[] rom = File.ReadAllBytes(args[0]);
await using var server = args.Length == 1 ? new LinkLobbyServer(port: 0) : null;
await using var pool = server == null ? null : new TetrisPlayerPool(server, rom, 4, log: Console.WriteLine);
if (pool != null)
{
    await pool.Ready.WaitAsync(TimeSpan.FromSeconds(30));
    await Task.Delay(100); var idle = pool.Snapshot(); await Task.Delay(500);
    if (idle.Length != 4 || pool.Snapshot().Any(p => p.Cycles != idle.Single(s => s.Number == p.Number).Cycles))
        throw new InvalidOperationException("Idle bots must stay frozen at the ready title screen.");
}
using var human = new EmulationHost(new Gameboy(enableDmgStartup: false), options: new() { HistoryCapacity = 0 });
human.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(Header.Start, Header.Length).ToArray()) }); human.Resume();
await using var client = await LinkLobbyClient.ConnectAsync(args.Length == 1 ? "127.0.0.1" : args[1], server?.Port ?? int.Parse(args[2]), "Human", "TETRIS", Convert.ToHexString(SHA256.HashData(rom)));
var clock = Stopwatch.StartNew(); var previous = clock.Elapsed;
byte State() => human.Inspection.ReadMemory(0xFFE1, 1)[0];
async Task Step()
{
    var now = clock.Elapsed; human.Advance(now - previous); previous = now;
    if (human.Fault != null) throw human.Fault;
    await Task.Delay(1);
}
async Task Until(Func<bool> predicate, string reason)
{
    var end = clock.Elapsed + TimeSpan.FromSeconds(20);
    while (!predicate() && clock.Elapsed < end) await Step();
    if (!predicate()) throw new TimeoutException($"{reason}; state={State():X2}; role={human.Inspection.ReadMemory(0xFFCB,1)[0]:X2}; bytes={client.BytesExchanged}; bots={string.Join(';',pool?.Snapshot() ?? [])}; error={client.Error}");
}
async Task Press(GameBoyButton button)
{
    human.SetButtonState(button, true); var end = clock.Elapsed + TimeSpan.FromMilliseconds(100);
    while (clock.Elapsed < end) await Step();
    human.ReleaseAll(); end = clock.Elapsed + TimeSpan.FromMilliseconds(100);
    while (clock.Elapsed < end) await Step();
}
await Until(() => client.State!.Session != Guid.Empty, "random matchmaking"); human.AttachSerialPeer(client.Peer);
await Until(() => State() == 7, "human title");
long titleReady = human.Statistics.Cycles + 70_224 * 60;
await Until(() => human.Statistics.Cycles >= titleReady, "human title ready for input");
await Press(GameBoyButton.Right); await Press(GameBoyButton.Start);
await Until(() => State() == 0x2B, "music menu");
long menuReady = human.Statistics.Cycles + 70_224 * 30;
await Until(() => human.Statistics.Cycles >= menuReady, "music input ready");
await Press(GameBoyButton.Start); await Until(() => State() == 0x17, "height menu");
menuReady = human.Statistics.Cycles + 70_224 * 30;
await Until(() => human.Statistics.Cycles >= menuReady, "height input ready");
await Press(GameBoyButton.Start); await Until(() => State() == 0x1A, "native 2PLAYER gameplay");
if (pool != null)
{
    var active = pool.Snapshot().Single(s => s.Session == client.State!.Session);
    Console.WriteLine($"Assigned CPU {active.Number}: {active.Style}. No UI/audio devices were created.");
    await Until(() => pool.Snapshot().Any(s => s.Session == client.State!.Session && s.Pieces >= 3), "headless AI placed pieces");
}
else
{
    var end = clock.Elapsed + TimeSpan.FromSeconds(10);
    while (clock.Elapsed < end) await Step();
    if (!client.Peer.IsConnected || State() != 0x1A) throw new InvalidOperationException("Remote headless match disconnected or left gameplay.");
}
if (client.BytesExchanged < 100) throw new InvalidOperationException("Too few cable transfers.");
Guid oldBot = client.State!.Players.Single(p => p.Id == client.State.Self).Partner;
client.Unlink(); human.AttachSerialPeer(null);
await Until(() => !client.State!.Players.Any(p => p.Id == oldBot), "used bot retired");
await Until(() => client.State!.Players.Count(p => p.Automated) == 4, "pool replenished with a fresh Game Boy");
if (client.State!.Session != Guid.Empty) throw new InvalidOperationException("Unplug caused an unwanted rematch.");
client.FindOpponent(); await Until(() => client.State!.Session != Guid.Empty, "explicit rematch");
Console.WriteLine($"PASS: {(pool == null ? "remote container multiplayer" : "four headless players and verified AI placement")}, {client.BytesExchanged} cable bytes, disconnect/reset and rematch.");
