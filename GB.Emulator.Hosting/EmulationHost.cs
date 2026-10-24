using System.Diagnostics;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Hosting;

/// <summary>
/// Owns one machine's playback and outputs. Serialize commands, callbacks and inspection on one
/// execution context. A UI schedules Advance and marshals immutable frames to its own dispatcher.
/// The supplied audio output is owned and disposed by this host.
/// </summary>
public sealed class EmulationHost : IDisposable, IButtonInput
{
    private readonly EmulationHostOptions options;
    private readonly IAudioOutput? audio;
    private readonly EmulationPacer pacer;
    private readonly HashSet<PauseReason> suspensions = new();
    private bool playbackRequested, disposed, advancing, muted, publishing, debugging, loading;
    private ushort? hitBreakpoint, skipBreakpoint;
    private long frames, sampleFrames, sampleCycles;
    private double sampleSeconds;
    private double fps, speed;
    private byte lastLcdControl;

    public EmulationHost(Gameboy? machine = null, IAudioOutput? audio = null, EmulationHostOptions? options = null)
    {
        this.options = options ?? new();
        if (this.options.MaximumInstructionsPerAdvance <= 0 || this.options.MaximumAdvanceDuration <= TimeSpan.Zero || this.options.HistoryCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        this.pacer = new EmulationPacer(this.options.MaximumCatchUpFrames);
        this.Machine = machine ?? new Gameboy(enableDmgStartup: true);
        if (this.Machine.Cartridge != null) throw new ArgumentException("Supply an unloaded machine; load cartridges through the host.", nameof(machine));
        this.audio = audio;
        this.Inspection = new(this);
        this.Debugging = new(this, this.options.HistoryCapacity);
        this.Machine.Video.FrameReady += this.OnFrameReady;
        if (audio != null) this.Machine.Sound.SamplesReady += this.OnSamplesReady;
    }

    internal Gameboy Machine { get; }
    internal string? Source { get; private set; }
    internal CartridgeSupport? Support { get; private set; }
    public EmulationInspection Inspection { get; }
    public EmulationDebugger Debugging { get; }
    public IButtonInput Input => this;
    public EmulationState State { get; private set; } = EmulationState.Empty;
    public bool IsPlaybackRequested => this.playbackRequested;
    public bool IsLoading => this.loading;
    public Exception? Fault { get; private set; }
    public VideoFrame? LatestFrame { get; private set; }
    public EmulationStatistics Statistics => new(this.frames, this.Machine.EmulatedCycles, this.fps, this.speed);
    public bool Muted
    {
        get => this.muted;
        set { this.EnsureUsable(); if (this.muted == value) return; this.muted = value; this.audio?.Flush(); }
    }
    public event Action<VideoFrame>? FrameReady;
    public event Action<EmulationState>? StateChanged;

    public Task LoadAsync(string path, CancellationToken cancellationToken = default) =>
        this.LoadAsync(new FileCartridgeSource(path), cancellationToken);

    public async Task LoadAsync(ICartridgeSource source, CancellationToken cancellationToken = default)
    {
        this.EnsureCommandAllowed();
        ArgumentNullException.ThrowIfNull(source);
        if (this.loading) throw new InvalidOperationException("A cartridge load is already in progress.");
        cancellationToken.ThrowIfCancellationRequested();
        this.loading = true;
        try
        {
            this.UpdateState();
            Cartridge cartridge = await source.LoadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            this.loading = false;
            this.Load(cartridge, source.Name);
        }
        finally
        {
            this.loading = false;
            if (!this.disposed) this.UpdateState();
        }
    }

    public void Load(Cartridge cartridge, string? source = null)
    {
        this.EnsureCommandAllowed();
        if (this.loading) throw new InvalidOperationException("A cartridge load is already in progress.");
        ArgumentNullException.ThrowIfNull(cartridge);
        if (cartridge.Data == null || cartridge.Data.Length < Header.Start + Header.Length)
            throw new CartridgeLoadException("The ROM is too short to contain a cartridge header.");
        // Own the ROM bytes and derive metadata from those bytes, never a caller's mutable header.
        byte[] data = (byte[])cartridge.Data.Clone();
        var owned = new Cartridge { Data = data, Header = Header.FromBytes(data.AsSpan(Header.Start, Header.Length).ToArray()) };
        CartridgeSupport support = CartridgeSupport.Assess(owned);
        if (!this.options.AllowUnsupportedCartridges && support.Level == CartridgeSupportLevel.Unsupported)
            throw new CartridgeLoadException("Unsupported cartridge hardware: " + string.Join(", ", support.Missing));
        this.playbackRequested = false;
        this.UpdateState();
        this.audio?.Flush();
        this.Machine.Load(owned);
        this.Source = source; this.Support = support; this.Fault = null;
        this.hitBreakpoint = this.skipBreakpoint = null;
        this.frames = 0; this.ResetTiming(); this.Debugging.ClearHistory();
        this.UpdateState(); this.PublishCurrentFrame();
    }

    public void Resume()
    {
        this.EnsureCommandAllowed();
        if (this.Fault != null) throw new InvalidOperationException("Reset or load a cartridge before resuming a faulted host.");
        if (this.Machine.Cartridge == null) throw new InvalidOperationException("Load a cartridge before playing.");
        this.skipBreakpoint = this.hitBreakpoint; this.hitBreakpoint = null;
        this.playbackRequested = true; this.UpdateState();
    }
    public void Pause()
    {
        this.EnsureCommandAllowed(); this.playbackRequested = false;
        this.Machine.Input.ReleaseAll(); this.UpdateState();
    }
    public void SetSuspended(PauseReason reason, bool suspended)
    {
        this.EnsureCommandAllowed();
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (suspended) this.suspensions.Add(reason); else this.suspensions.Remove(reason);
        this.UpdateState();
    }
    public void Reset()
    {
        this.EnsureCommandAllowed();
        if (this.loading) throw new InvalidOperationException("Wait for cartridge loading before resetting.");
        if (this.Machine.Cartridge == null) return;
        this.audio?.Flush(); this.Machine.Load(this.Machine.Cartridge);
        this.Fault = null; this.frames = 0; this.ResetTiming(); this.Debugging.ClearHistory();
        this.hitBreakpoint = this.skipBreakpoint = null;
        this.UpdateState(); this.PublishCurrentFrame();
    }

    public long Advance(TimeSpan elapsed)
    {
        this.EnsureCommandAllowed();
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (this.State != EmulationState.Running) return 0;
        this.Debugging.ClearHistory();
        this.advancing = true;
        long start = this.Machine.EmulatedCycles;
        try
        {
            int budget = this.pacer.AddElapsed(elapsed);
            long started = Stopwatch.GetTimestamp();
            for (int work = 0; work < this.options.MaximumInstructionsPerAdvance && this.Machine.EmulatedCycles - start < budget;)
            {
                ushort pc = this.Machine.Cpu.Registers.PC;
                bool skipCurrentBreakpoint = this.skipBreakpoint == pc;
                this.skipBreakpoint = null;
                if (!this.Machine.IsStartupSequenceActive && this.Debugging.ContainsBreakpoint(pc))
                {
                    if (!skipCurrentBreakpoint) { this.hitBreakpoint = pc; this.playbackRequested = false; break; }
                }
                int chunk = this.Debugging.HasBreakpoints ? 1 : 256;
                this.Machine.RunForCycles(budget - (this.Machine.EmulatedCycles - start), Math.Min(chunk, this.options.MaximumInstructionsPerAdvance - work));
                work += chunk;
                if (this.Machine.Cpu.IsStopped || Stopwatch.GetElapsedTime(started) >= this.options.MaximumAdvanceDuration) break;
            }
            long cycles = this.Machine.EmulatedCycles - start;
            if ((this.lastLcdControl & 0x80) != 0 && (this.Machine.Memory.Peek(0xFF40) & 0x80) == 0)
                this.PublishCurrentFrame(); // LCD-off produces no further VBlank; blank the presenter now.
            if (this.Machine.Cpu.IsStopped) this.pacer.Reset(); else this.pacer.Consume(cycles);
            this.SampleStatistics(elapsed);
            this.UpdateState();
            return cycles;
        }
        catch (Exception ex)
        {
            this.Fault = ex; this.playbackRequested = false;
            this.UpdateState(); throw;
        }
        finally { this.advancing = false; }
    }

    public void SetButtonState(GameBoyButton button, bool pressed) { this.EnsureUsable(); this.Machine.Input.SetButtonState(button, pressed); }
    public bool IsPressed(GameBoyButton button) { this.EnsureUsable(); return this.Machine.Input.IsPressed(button); }
    public void ReleaseAll() { this.EnsureUsable(); this.Machine.Input.ReleaseAll(); }

    private void OnFrameReady(object? sender, EventArgs e) { this.frames++; this.PublishCurrentFrame(); }
    internal void PublishCurrentFrame()
    {
        if (!this.options.CaptureFrames) return;
        VideoState video = this.Machine.CaptureVideoState();
        this.lastLcdControl = video.LcdControl;
        var frame = new VideoFrame(video, this.frames, this.Machine.EmulatedCycles, this.Machine.IsStartupSequenceActive);
        this.LatestFrame = frame;
        this.publishing = true;
        try { this.FrameReady?.Invoke(frame); }
        finally { this.publishing = false; }
    }
    private void OnSamplesReady(ReadOnlyMemory<short> samples)
    {
        if (!this.muted && (this.State == EmulationState.Running || this.debugging)) this.audio?.Write(samples);
    }
    private void UpdateState()
    {
        EmulationState next = this.Fault != null ? EmulationState.Faulted : this.Machine.Cartridge == null ? EmulationState.Empty :
            this.playbackRequested && !this.loading && this.suspensions.Count == 0 ? EmulationState.Running : EmulationState.Paused;
        if (this.State == next) return;
        this.State = next; this.ResetTiming();
        if (next != EmulationState.Running) { this.Machine.Input.ReleaseAll(); this.audio?.Flush(); }
        this.StateChanged?.Invoke(next);
    }
    internal void ResetTiming()
    {
        this.pacer.Reset(); this.sampleSeconds = this.fps = this.speed = 0;
        this.sampleCycles = this.Machine.EmulatedCycles; this.sampleFrames = this.frames;
    }
    private void SampleStatistics(TimeSpan elapsed)
    {
        this.sampleSeconds += elapsed.TotalSeconds;
        if (this.sampleSeconds < 1) return;
        this.fps = (this.frames - this.sampleFrames) / this.sampleSeconds;
        this.speed = (this.Machine.EmulatedCycles - this.sampleCycles) / (this.sampleSeconds * EmulationPacer.CyclesPerSecond);
        this.sampleSeconds = 0; this.sampleCycles = this.Machine.EmulatedCycles; this.sampleFrames = this.frames;
    }
    internal void EnsureUsable() => ObjectDisposedException.ThrowIf(this.disposed, this);
    internal void EnsureCommandAllowed()
    {
        this.EnsureUsable();
        if (this.advancing || this.publishing) throw new InvalidOperationException("Queue commands until execution returns; host callbacks cannot reenter playback.");
    }
    internal long FrameCount { get => this.frames; set => this.frames = value; }
    internal CpuStepResult ExecuteDebugStep()
    {
        this.advancing = this.debugging = true;
        try { return this.Machine.Step(); }
        finally { this.advancing = this.debugging = false; }
    }
    internal void FlushAudio() => this.audio?.Flush();
    public void Dispose()
    {
        if (this.disposed) return;
        if (this.advancing || this.publishing) throw new InvalidOperationException("Dispose the host after execution returns.");
        this.Debugging.ClearHistory();
        this.disposed = true;
        this.Machine.Video.FrameReady -= this.OnFrameReady;
        if (this.audio != null) this.Machine.Sound.SamplesReady -= this.OnSamplesReady;
        this.Machine.Input.ReleaseAll();
        try { this.audio?.Flush(); }
        finally
        {
            try { this.audio?.Dispose(); }
            finally { this.State = EmulationState.Disposed; this.StateChanged = null; this.FrameReady = null; }
        }
    }
}
