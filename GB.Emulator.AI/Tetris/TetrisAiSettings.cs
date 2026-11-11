using System.Collections.ObjectModel;
using System.Text.Json;

namespace GB.Emulator.AI.Tetris;

/// <summary>Immutable playing policy. All timings are guest frames, independent of UI/network speed.</summary>
public sealed record TetrisAiSettings
{
    public int Version { get; init; } = 1;
    public double ClearLines { get; init; } = .76;
    public double TetrisBonus { get; init; }
    public double AvoidHoles { get; init; } = .76;
    public double LowStack { get; init; } = .51;
    public double FlatSurface { get; init; } = .18;
    public double CeilingSafety { get; init; }
    public double Lookahead { get; init; }
    public int ReleaseFrames { get; init; } = 2;
    public int HoldFrames { get; init; } = 2;

    public void Validate()
    {
        static bool Range(double value, double max) => double.IsFinite(value) && value >= 0 && value <= max;
        if (Version != 1 || !Range(ClearLines, 4) || !Range(TetrisBonus, 8) || !Range(AvoidHoles, 8) ||
            !Range(LowStack, 2) || !Range(FlatSurface, 2) || !Range(CeilingSafety, 4) || !Range(Lookahead, 1) ||
            ReleaseFrames is < 1 or > 20 || HoldFrames is < 1 or > 4)
            throw new ArgumentException("AI settings contain an unsupported version or an out-of-range weight/timing.");
    }

    public static TetrisAiSettings Load(string path)
    {
        if (new FileInfo(path).Length > 16_384) throw new InvalidDataException("AI settings must fit in 16 KiB.");
        var settings = JsonSerializer.Deserialize<TetrisAiSettings>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Empty AI settings.");
        settings.Validate();
        return settings;
    }

    public void Save(string path)
    {
        Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyDictionary<string, TetrisAiSettings> Presets { get; } =
        new ReadOnlyDictionary<string, TetrisAiSettings>(new Dictionary<string, TetrisAiSettings>
        {
            ["Original"] = new(),
            ["Cautious"] = new() { AvoidHoles = 2.5, LowStack = .75, FlatSurface = .25, CeilingSafety = 1, Lookahead = .65 },
            ["Tetris hunter"] = new() { ClearLines = 1, TetrisBonus = 4, AvoidHoles = 2, LowStack = .35, CeilingSafety = .8, Lookahead = .8 },
            ["Strategist"] = new() { AvoidHoles = 2, CeilingSafety = .8, Lookahead = .8, HoldFrames = 1, ReleaseFrames = 1 }
        });
}
