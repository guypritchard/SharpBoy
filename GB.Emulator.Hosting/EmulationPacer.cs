namespace GB.Emulator.Hosting;

/// <summary>Shared cycle budget, retaining fractional cycles and instruction overshoot.</summary>
public sealed class EmulationPacer
{
    public const int CyclesPerSecond = 4_194_304;
    public const int CyclesPerFrame = 70_224;
    private readonly int maximumCatchUpFrames;
    private double debt;
    public EmulationPacer(int maximumCatchUpFrames = 2)
    {
        if (maximumCatchUpFrames is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(maximumCatchUpFrames));
        this.maximumCatchUpFrames = maximumCatchUpFrames;
    }
    public int AddElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        this.debt = Math.Min(CyclesPerFrame * this.maximumCatchUpFrames,
            this.debt + elapsed.TotalSeconds * CyclesPerSecond);
        return Math.Max(0, (int)this.debt);
    }
    public void Consume(long cycles) => this.debt -= cycles;
    public void Reset() => this.debt = 0;
}
