using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;

namespace GB.Emulator.AI.Tetris;

/// <summary>Reobserves between button pulses; never executes a stale queued route.</summary>
public sealed class TetrisController(ITetrisObserver observer)
{
    private readonly TetrisPlanner planner = new();
    private GameBoyButton? pressed;
    private int phase;
    private long previousCycles = -1;
    private CancellationTokenSource? searchCancellation;
    private Task<TetrisPlan?>? search;
    private TetrisObservation? searchObservation;
    private bool settingsChanged;
    public TetrisAiSettings Settings { get; private set; } = new();
    public TetrisPlan? Plan { get; private set; }
    public TetrisObservation? Observation { get; private set; }
    public bool IsThinking => search is { IsCompleted: false };

    /// <summary>Call on the host owner. The next tick releases held input and replaces the old search.</summary>
    public void Configure(TetrisAiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        Settings = settings; settingsChanged = true; CancelSearch(); Plan = null;
    }

    public void Tick(EmulationHost host)
    {
        if (host.State != EmulationState.Running) { Stop(host); return; }
        if (settingsChanged) { Stop(host); settingsChanged = false; }
        // Guest time is stationary; preserve the pulse/search until the clock resumes.
        if (host.IsWaitingForSerialPeer) return;
        // Called once per UI tick, but pacing is in guest time, including at a serial rendezvous.
        long cycles = host.Statistics.Cycles;
        if (cycles < previousCycles) Stop(host);
        if (previousCycles >= 0 && cycles - previousCycles < 70224) return;
        previousCycles = cycles;
        if (pressed is { } key)
        {
            if (++phase < Settings.HoldFrames) return;
            host.SetButtonState(key, false); pressed = null; phase = 0; return;
        }
        if (++phase < Settings.ReleaseFrames) return;
        var observation = Observation = observer.Observe(host.Inspection);
        if (observation == null) { CancelSearch(); Plan = null; return; }
        TetrisPlan? chosen = null;
        if (search != null)
        {
            if (!search.IsCompleted) return;
            var completed = search.GetAwaiter().GetResult();
            var source = searchObservation!;
            CancelSearch();
            // Reject a replaced piece/board, including rows added by the remote opponent.
            // Gravity may have advanced during planning; the first move is checked below.
            if (SamePositionFamily(source, observation)) chosen = completed;
        }
        if (chosen == null)
        {
            StartSearch(observation);
            return;
        }
        Plan = chosen;
        phase = 0;
        // A terminal placement still needs soft drop until the game locks it.
        var move = Plan.Moves.Length == 0 ? TetrisMove.Down : Plan.Moves[0];
        if (!Legal(observation, move)) { Plan = null; StartSearch(observation); return; }
        pressed = move switch
        {
            TetrisMove.Left => GameBoyButton.Left,
            TetrisMove.Right => GameBoyButton.Right,
            TetrisMove.Clockwise => GameBoyButton.A,
            TetrisMove.Counterclockwise => GameBoyButton.B,
            _ => GameBoyButton.Down
        };
        host.SetButtonState(pressed.Value, true);
    }

    private void StartSearch(TetrisObservation observation)
    {
        searchCancellation = new();
        var token = searchCancellation.Token;
        var settings = Settings;
        searchObservation = observation;
        // Search consumes detached snapshots only; emulation and audio never wait for it.
        search = Task.Run(() => planner.Choose(observation, settings, token), token);
        _ = search.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private void CancelSearch()
    {
        searchCancellation?.Cancel(); searchCancellation?.Dispose();
        searchCancellation = null; search = null; searchObservation = null;
    }

    private static bool SamePositionFamily(TetrisObservation a, TetrisObservation b) =>
        a.X == b.X && a.Rotation == b.Rotation && b.Y >= a.Y && a.Board.AsSpan().SequenceEqual(b.Board) &&
        SameShape(a.Rotations, b.Rotations) && SameShape(a.NextRotations, b.NextRotations) && a.NextRotation == b.NextRotation;

    private static bool SameShape(Cell[][]? a, Cell[][]? b) => ReferenceEquals(a, b) ||
        (a != null && b != null && a.Length == b.Length && a.Zip(b).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)));

    private static bool Legal(TetrisObservation observation, TetrisMove move)
    {
        int rotation = observation.Rotation, x = observation.X, y = observation.Y;
        if (move == TetrisMove.Clockwise) rotation = (rotation + 1) % observation.Rotations.Length;
        else if (move == TetrisMove.Counterclockwise) rotation = (rotation + observation.Rotations.Length - 1) % observation.Rotations.Length;
        else if (move == TetrisMove.Left) x--;
        else if (move == TetrisMove.Right) x++;
        else return true; // Soft drop is harmless at a landing; it asks the ROM to lock.
        return observation.Rotations[rotation].All(cell =>
        {
            int cx = cell.X + x, cy = cell.Y + y;
            return cx >= 0 && cx < 10 && cy < 20 && (cy < 0 || !observation.Board[cy * 10 + cx]);
        });
    }

    public void Stop(EmulationHost host)
    {
        if (pressed is { } key) host.SetButtonState(key, false);
        CancelSearch();
        pressed = null; phase = 0; previousCycles = -1; Plan = null; Observation = null;
    }
}
