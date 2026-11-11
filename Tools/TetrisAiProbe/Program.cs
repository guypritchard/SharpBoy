using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using GB.Emulator.AI.Tetris;
using GB.Emulator.Link;
using System.Security.Cryptography;
if (args.Length != 1) { Console.Error.WriteLine("Usage: TetrisAiProbe <private Tetris ROM path>"); return 2; }
var rom = File.ReadAllBytes(args[0]);
if (!DmgTetrisObserver.Supports(rom)) throw new InvalidDataException("This ROM revision has no verified Tetris reader.");
using var a = new EmulationHost(new Gameboy(enableDmgStartup: false), options: new() { HistoryCapacity = 0, MaximumAdvanceDuration = TimeSpan.FromMilliseconds(30) });
using var b = new EmulationHost(new Gameboy(enableDmgStartup: false), options: new() { HistoryCapacity = 0, MaximumAdvanceDuration = TimeSpan.FromMilliseconds(30) });
var cart = new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, 0x50).ToArray()) }; a.Load(cart); b.Load(cart); a.Resume(); b.Resume();
await using var server = new LinkLobbyServer(port: 0);
await using var ca = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Human", "TETRIS", Convert.ToHexString(SHA256.HashData(rom)));
await using var cb = await LinkLobbyClient.ConnectAsync("127.0.0.1", server.Port, "Bot", "TETRIS", Convert.ToHexString(SHA256.HashData(rom)));
ca.Invite(cb.State!.Self); await Task.Delay(30); cb.Accept(ca.State!.Self); await Task.Delay(30); a.AttachSerialPeer(ca.Peer); b.AttachSerialPeer(cb.Peer);
byte Read(EmulationHost h, int address) => h.Inspection.ReadMemory((ushort)address, 1)[0];
void Tick() { a.Advance(TimeSpan.FromSeconds(1.0 / 60)); b.Advance(TimeSpan.FromSeconds(1.0 / 60)); }
async Task Run(int frames) { for (int f = 0; f < frames; f++) { Tick(); await Task.Delay(1); } }
async Task Press(EmulationHost h, GameBoyButton key) { h.SetButtonState(key, true); await Run(6); h.ReleaseAll(); await Run(6); }
string Info() => $"states {Read(a, 0xffe1):X2}/{Read(b, 0xffe1):X2} role {Read(a, 0xffcb):X2}/{Read(b, 0xffcb):X2} wait {a.IsWaitingForSerialPeer}/{b.IsWaitingForSerialPeer} bytes {ca.BytesExchanged}/{cb.BytesExchanged} SC {Read(a, 0xff02):X2}/{Read(b, 0xff02):X2} error {ca.Error}/{cb.Error}";
await Run(500); Console.WriteLine(Info());
await Press(a, GameBoyButton.Right); await Press(b, GameBoyButton.Right); Console.WriteLine(Info());
await Press(a, GameBoyButton.Start); await Run(120); Console.WriteLine(Info());
await Press(a, GameBoyButton.Start); await Run(120); Console.WriteLine(Info());
await Press(a, GameBoyButton.Start); await Press(b, GameBoyButton.Start); await Run(300); Console.WriteLine(Info());
var ai = new TetrisController(new DmgTetrisObserver()); int plans = 0;
for (int frame = 0; frame < 4000; frame++) { Tick(); ai.Tick(b); if (ai.Plan != null) plans++; if (frame % 500 == 0) Console.WriteLine($"f{frame} plans{plans} pieces{Read(b, 0xffb0)} lines{Read(b, 0xff9e)} " + Info()); await Task.Delay(1); }
Console.WriteLine($"FINAL plans{plans} pieces{Read(b, 0xffb0)} lines{Read(b, 0xff9e)} " + Info());

if (plans == 0 || Read(b, 0xffb0) < 10 || Read(b, 0xff9e) == 0x30) throw new InvalidOperationException("The AI did not place pieces and clear lines.");
return 0;
