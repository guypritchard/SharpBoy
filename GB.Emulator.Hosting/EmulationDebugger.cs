using GB.Emulator.Core;

namespace GB.Emulator.Hosting;

/// <summary>Opt-in stepping, breakpoints and bounded rewind, separate from playback and UI history.</summary>
public sealed class EmulationDebugger
{
    private readonly EmulationHost host;
    private readonly int capacity;
    private readonly List<(GameboyState Machine, long Frames)> history = new();
    private readonly HashSet<ushort> breakpoints = new();
    internal EmulationDebugger(EmulationHost host, int capacity) { this.host = host; this.capacity = capacity; }
    public int HistoryCount => this.history.Count;
    public bool IsAtBreakpoint => this.breakpoints.Contains(this.host.Inspection.CaptureCpu().PC);
    internal bool HasBreakpoints => this.breakpoints.Count != 0;
    public void SetBreakpoint(ushort address, bool enabled)
    {
        this.host.EnsureCommandAllowed();
        if (enabled) this.breakpoints.Add(address); else this.breakpoints.Remove(address);
    }
    public CpuStepResult Step()
    {
        this.EnsurePaused();
        if (this.host.Machine.Cartridge == null) throw new InvalidOperationException("Load a cartridge before stepping.");
        var checkpoint = (Machine: this.host.Machine.CaptureState(), Frames: this.host.FrameCount);
        try
        {
            CpuStepResult result = this.host.ExecuteDebugStep();
            this.host.PublishCurrentFrame(); this.host.ResetTiming();
            if (this.capacity > 0)
            {
                if (this.history.Count == this.capacity) this.history.RemoveAt(0);
                this.history.Add(checkpoint);
            }
            return result;
        }
        catch
        {
            this.host.Machine.RestoreState(checkpoint.Machine);
            this.host.FrameCount = checkpoint.Frames;
            this.host.ResetTiming(); this.host.FlushAudio();
            throw;
        }
    }
    public bool StepBack()
    {
        this.EnsurePaused();
        if (this.history.Count == 0) return false;
        this.host.FlushAudio();
        this.host.Machine.RestoreState(this.history[^1].Machine);
        this.host.FrameCount = this.history[^1].Frames;
        this.history.RemoveAt(this.history.Count - 1);
        this.host.ResetTiming(); this.host.PublishCurrentFrame(); return true;
    }
    public IReadOnlyList<CpuStepResult> GetDisassembly() { this.host.EnsureUsable(); return this.host.Machine.GetDisassembly(); }
    public void ClearHistory() { this.host.EnsureUsable(); this.history.Clear(); }
    internal bool ContainsBreakpoint(ushort address) => this.breakpoints.Contains(address);
    private void EnsurePaused()
    {
        this.host.EnsureCommandAllowed();
        if (this.host.IsLoading) throw new InvalidOperationException("Wait for cartridge loading before debugging.");
        if (this.host.State is EmulationState.Running or EmulationState.Faulted)
            throw new InvalidOperationException("Pause or reset the host before stepping or rewinding.");
    }
}
