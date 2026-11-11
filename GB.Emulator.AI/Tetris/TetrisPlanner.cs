namespace GB.Emulator.AI.Tetris;

public readonly record struct Cell(int X, int Y);
public enum TetrisMove { Left, Right, Clockwise, Counterclockwise, Down }

/// <summary>Settled cells only; rotations use the ROM's origin and rotation order.</summary>
public sealed record TetrisObservation(bool[] Board, Cell[][] Rotations, int X, int Y, int Rotation,
    Cell[][]? NextRotations = null, int NextX = 4, int NextY = 3, int NextRotation = 0);
public sealed record TetrisPlan(TetrisMove[] Moves, Cell[] Landing, int Lines, int Holes, int Height, double Score);

/// <summary>Searches reachable placements with no wall kicks, hard drop or RAM writes.</summary>
public sealed class TetrisPlanner
{
    public const int Width = 10, Height = 20;
    private readonly record struct Position(int X, int Y, int Rotation);

    public TetrisPlan? Choose(TetrisObservation observation, TetrisAiSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= new(); settings.Validate();
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Board.Length != Width * Height || observation.Rotations.Length is < 1 or > 4 ||
            observation.Rotations.Any(r => r.Length != 4 || r.Distinct().Count() != 4 || r.Any(c => c.X is < -4 or > 4 || c.Y is < -4 or > 4)) ||
            observation.Rotation < 0 || observation.Rotation >= observation.Rotations.Length)
            throw new ArgumentException("Expected a 10x20 settled board and one to four four-cell rotations.");

        bool Fits(Position p) => p.Y >= -4 && p.Y < Height && observation.Rotations[p.Rotation].All(c =>
        {
            int x = p.X + c.X, y = p.Y + c.Y;
            return x >= 0 && x < Width && y < Height && (y < 0 || !observation.Board[y * Width + x]);
        });
        var initial = new Position(observation.X, observation.Y, observation.Rotation);
        if (!Fits(initial)) return null;
        var queue = new Queue<Position>();
        var parents = new Dictionary<Position, (Position Previous, TetrisMove Move)>();
        var visited = new HashSet<Position> { initial };
        queue.Enqueue(initial);
        TetrisPlan? best = null;
        while (queue.TryDequeue(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Fits(current with { Y = current.Y + 1 }))
            {
                var cells = observation.Rotations[current.Rotation].Select(c => new Cell(current.X + c.X, current.Y + c.Y)).ToArray();
                if (cells.All(c => c.Y >= 0))
                {
                    var board = (bool[])observation.Board.Clone();
                    foreach (var c in cells) board[c.Y * Width + c.X] = true;
                    int lines = ClearLines(board);
                    var (score, holes, height) = Evaluate(board, lines, settings);
                    if (settings.Lookahead > 0 && observation.NextRotations is { } next)
                    {
                        // Full reachable search for the visible preview, bounded to one extra ply.
                        var future = Choose(new(board, next, observation.NextX, observation.NextY, observation.NextRotation),
                            settings with { Lookahead = 0 }, cancellationToken);
                        score = future == null ? double.NegativeInfinity : score + settings.Lookahead * future.Score;
                    }
                    var moves = new List<TetrisMove>();
                    for (var p = current; p != initial; p = parents[p].Previous) moves.Add(parents[p].Move);
                    moves.Reverse();
                    // Prefer shorter routes on equal scores, reducing exposure to gravity.
                    if (best == null || score > best.Score || (score == best.Score && moves.Count < best.Moves.Length))
                        best = new(moves.ToArray(), cells, lines, holes, height, score);
                }
            }
            Visit(current with { X = current.X - 1 }, TetrisMove.Left);
            Visit(current with { X = current.X + 1 }, TetrisMove.Right);
            Visit(current with { Rotation = (current.Rotation + 1) % observation.Rotations.Length }, TetrisMove.Clockwise);
            Visit(current with { Rotation = (current.Rotation + observation.Rotations.Length - 1) % observation.Rotations.Length }, TetrisMove.Counterclockwise);
            Visit(current with { Y = current.Y + 1 }, TetrisMove.Down);

            void Visit(Position next, TetrisMove move)
            {
                if (!Fits(next) || !visited.Add(next)) return;
                parents[next] = (current, move);
                queue.Enqueue(next);
            }
        }
        return best;
    }

    private static (double Score, int Holes, int Height) Evaluate(bool[] board, int lines, TetrisAiSettings settings)
    {
        int holes = 0, height = 0, bumpiness = 0, lastHeight = 0, maximum = 0;
        for (int x = 0; x < Width; x++)
        {
            int columnHeight = 0;
            for (int y = 0; y < Height; y++)
            {
                if (board[y * Width + x] && columnHeight == 0) columnHeight = Height - y;
                else if (!board[y * Width + x] && columnHeight > 0) holes++;
            }
            height += columnHeight; maximum = Math.Max(maximum, columnHeight);
            if (x > 0) bumpiness += Math.Abs(columnHeight - lastHeight);
            lastHeight = columnHeight;
        }
        int danger = Math.Max(0, maximum - 12);
        double score = lines * settings.ClearLines + (lines == 4 ? settings.TetrisBonus : 0) - height * settings.LowStack -
            holes * settings.AvoidHoles - bumpiness * settings.FlatSurface - danger * danger * settings.CeilingSafety;
        return (score, holes, height);
    }

    private static int ClearLines(bool[] board)
    {
        int destination = Height - 1, lines = 0;
        for (int source = Height - 1; source >= 0; source--)
        {
            if (Enumerable.Range(0, Width).All(x => board[source * Width + x])) { lines++; continue; }
            Array.Copy(board, source * Width, board, destination-- * Width, Width);
        }
        if (destination >= 0) Array.Clear(board, 0, (destination + 1) * Width);
        return lines;
    }
}
