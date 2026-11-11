using System.Diagnostics;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using GB.Emulator.AI.Tetris;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using GB.Emulator.Link;

namespace GB.Link.Server;

public sealed record TetrisPlayerStatus(int Number, string Style, Guid Session, long Cycles, byte Pieces, byte State, byte Role);

/// <summary>Server-owned, silent Game Boys. Each worker serializes all access to its own host.</summary>
public sealed class TetrisPlayerPool : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly Task[] workers;
    private readonly Task ready;
    private readonly ConcurrentDictionary<int, TetrisPlayerStatus> status = new();
    public Task Ready => ready;
    /// <summary>Detached service diagnostics; never a live machine or memory reference.</summary>
    public TetrisPlayerStatus[] Snapshot() => status.Values.OrderBy(s => s.Number).ToArray();

    public TetrisPlayerPool(LinkLobbyServer server, byte[] rom, int count, string address = "127.0.0.1",
        bool tls = false, string? accessToken = null, Action<string>? log = null)
    {
        if (count is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(count), "Use 1–12 headless players (the hub has 32 player slots).");
        if (!DmgTetrisObserver.Supports(rom)) throw new ArgumentException("No verified AI reader exists for this Tetris ROM revision.", nameof(rom));
        byte[] ownedRom = (byte[])rom.Clone();
        var started = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var presets = TetrisAiSettings.Presets.ToArray();
        workers = Enumerable.Range(0, count).Select(index => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { await Play(index, presets[index % presets.Length], started[index]); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex) { started[index].TrySetException(ex); log?.Invoke($"Player {index + 1}: {ex.Message}; restarting."); }
                try { await Task.Delay(250, stop.Token); } catch (OperationCanceledException) { break; }
            }
            started[index].TrySetCanceled();
        })).ToArray();
        ready = Task.WhenAll(started.Select(s => s.Task));

        async Task Play(int index, KeyValuePair<string, TetrisAiSettings> preset, TaskCompletionSource started)
        {
            using var host = new EmulationHost(new Gameboy(enableDmgStartup: false), options: new() { HistoryCapacity = 0 });
            var ai = new TetrisController(new DmgTetrisObserver()); ai.Configure(preset.Value);
            host.Load(new Cartridge { Data = ownedRom, Header = Header.FromBytes(ownedRom.AsSpan(Header.Start, Header.Length).ToArray()) });
            host.Resume();
            var clock = Stopwatch.StartNew(); TimeSpan previous = clock.Elapsed;
            async Task Step(LinkLobbyClient? client = null)
            {
                var now = clock.Elapsed; host.Advance(now - previous); previous = now;
                if (host.Fault != null) throw new InvalidOperationException("Headless Game Boy faulted.", host.Fault);
                if (client != null) ai.Tick(host);
                await Task.Delay(client != null && client.State?.Session == Guid.Empty ? 25 : 1, stop.Token);
            }
            try
            {
                while (host.Inspection.ReadMemory(0xFFE1, 1)[0] != 7)
                {
                    if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Tetris title screen did not appear.");
                    await Step();
                }
                // Choose 2PLAYER before advertising readiness. The human supplies Start/menu selections over the cable.
                long titleReady = host.Statistics.Cycles + 70_224 * 60;
                while (host.Statistics.Cycles < titleReady) await Step();
                host.SetButtonState(GameBoyButton.Right, true);
                var releaseAt = clock.Elapsed + TimeSpan.FromMilliseconds(100);
                while (clock.Elapsed < releaseAt) await Step();
                host.ReleaseAll();
                releaseAt = clock.Elapsed + TimeSpan.FromMilliseconds(100);
                while (clock.Elapsed < releaseAt) await Step();

                await using var client = await LinkLobbyClient.ConnectAsync(address, server.Port, $"CPU {index + 1:00} · {preset.Key}",
                    host.Inspection.CaptureCartridge()!.Title, Convert.ToHexString(SHA256.HashData(ownedRom)), stop.Token, tls, accessToken, autoMatch: false);
                // Preserve the ready title indefinitely; idle ROMs otherwise enter their attract-mode demo.
                host.Pause();
                server.RegisterAutomatedPlayer(client.State!.Self);
                log?.Invoke($"CPU {index + 1:00} ready ({preset.Key})."); started.TrySetResult();
                Guid attached = Guid.Empty;
                byte previousState = byte.MaxValue;
                long nextMenuTap = 0, releaseMenuTap = 0;
                while (client.IsConnected && !stop.IsCancellationRequested)
                {
                    Guid session = client.State!.Session;
                    if (attached != session)
                    {
                        if (attached != Guid.Empty) break; // Dispose this machine; next opponent gets a fresh title screen.
                        host.AttachSerialPeer(session == Guid.Empty ? null : client.Peer); attached = session;
                        if (session != Guid.Empty) host.Resume();
                    }
                    await Step(client);
                    byte gameState = host.Inspection.ReadMemory(0xFFE1, 1)[0];
                    long cycles = host.Statistics.Cycles;
                    // Each player confirms their own starting height. Supply real Start taps,
                    // including a bounded retry if the first tap coincides with a menu transition.
                    if (gameState != previousState) nextMenuTap = cycles + 70_224 * 20;
                    if (releaseMenuTap != 0 && (cycles >= releaseMenuTap || gameState != 0x17))
                    { host.SetButtonState(GameBoyButton.Start, false); releaseMenuTap = 0; }
                    if (gameState == 0x17 && releaseMenuTap == 0 && cycles >= nextMenuTap)
                    {
                        host.SetButtonState(GameBoyButton.Start, true);
                        releaseMenuTap = cycles + 70_224 * 6; nextMenuTap = cycles + 70_224 * 60;
                    }
                    previousState = gameState;
                    status[index] = new(index + 1, preset.Key, attached, host.Statistics.Cycles, host.Inspection.ReadMemory(0xFFB0, 1)[0],
                        host.Inspection.ReadMemory(0xFFE1, 1)[0], host.Inspection.ReadMemory(0xFFCB, 1)[0]);
                }
            }
            finally { status.TryRemove(index, out _); ai.Stop(host); host.AttachSerialPeer(null); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await Task.WhenAll(workers); } finally { stop.Dispose(); }
    }
}
