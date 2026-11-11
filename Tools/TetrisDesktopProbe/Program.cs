using System.Diagnostics;
using GB.Emulator.AI.Tetris;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using GB.Emulator.Windows;

namespace GB.Emulator.DesktopProbe;

/// <summary>Explicit local validation of the actual WinForms/WPF timers, audio and remote relay.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: TetrisDesktopProbe ROM.gb [server] [port] [seconds] [--keep-open] [--preset name] [--mute]");
            return 2;
        }
        if (!DmgTetrisObserver.Supports(System.IO.File.ReadAllBytes(args[0])))
            throw new InvalidOperationException("This probe requires the verified Tetris ROM revision.");
        string address = args.ElementAtOrDefault(1) ?? "127.0.0.1";
        int port = int.Parse(args.ElementAtOrDefault(2) ?? "8766");
        int seconds = int.Parse(args.ElementAtOrDefault(3) ?? "30");
        bool keepOpen = args.Contains("--keep-open");
        bool mute = args.Contains("--mute");
        int presetIndex = Array.IndexOf(args, "--preset");
        var style = presetIndex < 0 ? new TetrisAiSettings() : TetrisAiSettings.Presets[args[presetIndex + 1]];
        int result = 1;
        System.Windows.Forms.Application.EnableVisualStyles();
        using var player = new PlayerForm(args[0]) { PauseWhenInactive = false };
        player.Host.Muted = mute;
        player.Shown += async (_, _) =>
        {
            try
            {
                await Until(() => player.Host.Inspection.CaptureCartridge() != null, "ROM load");
                player.ShowLinkLobby();
                var lobby = player.LinkLobby!;
                await lobby.JoinAsync(address, port, "Player one", autoMatch: false);
                if (lobby.Client == null) throw new InvalidOperationException(lobby.ConnectionStatus);
                await lobby.AddOpponentAsync();
                var opponent = lobby.Opponent ?? throw new InvalidOperationException(lobby.ConnectionStatus);
                opponent.ConfigureStyle(style);
                await Until(() => opponent.Client?.State != null, "opponent join");
                lobby.Client.Invite(opponent.Client!.State!.Self);
                await Until(() => lobby.Client.State!.Session != Guid.Empty && opponent.Client.Peer.IsConnected, "cable pairing");
                await Until(() => State(player.Host) == 7 && State(opponent.Host) == 7, "both title screens");
                await Press(player.Host, GameBoyButton.Right);
                await Press(player.Host, GameBoyButton.Start);
                await Until(() => State(player.Host) == 0x2B && State(opponent.Host) == 0x2B, "music menu");
                await Press(player.Host, GameBoyButton.Start);
                await Until(() => State(player.Host) == 0x17 && State(opponent.Host) == 0x17, "height menu");
                await Press(player.Host, GameBoyButton.Start);
                await Until(() => State(player.Host) == 0x1A && State(opponent.Host) == 0x1A, "native multiplayer gameplay");
                // Leave the lobby running without covering either LCD.
                lobby.WindowState = System.Windows.WindowState.Minimized;
                Console.WriteLine("Both native games entered multiplayer using the actual desktop schedulers.");
                var clock = Stopwatch.StartNew();
                long cycles = player.Host.Statistics.Cycles, frames = player.Host.Statistics.Frames;
                var audioStart = player.AudioStatistics;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    await Task.Delay(1000);
                    if (!lobby.Client.Peer.IsConnected || player.Host.Fault != null || opponent.Host.Fault != null)
                        throw new InvalidOperationException($"Link/host failure: {lobby.Client.Error}; {opponent.Client.Error}; {player.Host.Fault}; {opponent.Host.Fault}");
                    double speed = (player.Host.Statistics.Cycles - cycles) / (clock.Elapsed.TotalSeconds * EmulationPacer.CyclesPerSecond);
                    double fps = (player.Host.Statistics.Frames - frames) / clock.Elapsed.TotalSeconds;
                    Console.WriteLine($"{clock.Elapsed.TotalSeconds:F1}s speed={speed:P1} FPS={fps:F1} bytes={lobby.Client.BytesExchanged} states={State(player.Host):X2}/{State(opponent.Host):X2} bot-pieces={opponent.Host.Inspection.ReadMemory(0xFFB0, 1)[0]} audio={player.AudioStatistics}");
                }
                double measured = (player.Host.Statistics.Cycles - cycles) / (clock.Elapsed.TotalSeconds * EmulationPacer.CyclesPerSecond);
                if (measured < .85) throw new InvalidOperationException($"Playback too slow: {measured:P1}.");
                var audioEnd = player.AudioStatistics;
                if (!mute && audioStart != null && audioEnd != null)
                {
                    Console.WriteLine($"Gameplay audio: underruns={audioEnd.Underruns - audioStart.Underruns}, discarded blocks={audioEnd.DiscardedBlocks - audioStart.DiscardedBlocks}.");
                    if (audioEnd.Underruns != audioStart.Underruns || audioEnd.DiscardedBlocks != audioStart.DiscardedBlocks)
                        throw new InvalidOperationException("The Windows audio queue starved or dropped PCM during gameplay.");
                }
                else Console.WriteLine("Windows audio muted/unavailable; only link and guest-clock throughput were verified.");
                result = 0;
                Console.WriteLine("PASS: desktop link remained connected with at least 85% native clock throughput.");
                if (keepOpen)
                {
                    await Press(player.Host, GameBoyButton.Start);
                    await Until(() => player.Host.Inspection.ReadMemory(0xFFAB, 1)[0] != 0 &&
                        opponent.Host.Inspection.ReadMemory(0xFFAB, 1)[0] != 0, "both games paused for handoff");
                    Console.WriteLine("Both players left at a paused multiplayer match. Press Enter in player one to play.");
                    return;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            player.Close();
        };
        System.Windows.Forms.Application.Run(player);
        return result;
    }

    private static byte State(EmulationHost host) => host.Inspection.ReadMemory(0xFFE1, 1)[0];
    private static async Task Press(EmulationHost host, GameBoyButton button)
    {
        host.SetButtonState(button, true);
        try { await Task.Delay(80); }
        finally { host.SetButtonState(button, false); }
        await Task.Delay(80);
    }
    private static async Task Until(Func<bool> condition, string operation)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("Timed out waiting for " + operation);
    }
}
