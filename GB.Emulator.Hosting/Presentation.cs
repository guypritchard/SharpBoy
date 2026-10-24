using GB.Emulator.Core;
using GB.Emulator.Core.Rendering;

namespace GB.Emulator.Hosting;

/// <summary>Consumes PCM synchronously. Queueing implementations must copy the samples before returning.</summary>
public interface IAudioOutput : IDisposable
{
    void Write(ReadOnlyMemory<short> samples);
    void Flush();
}

public interface IVideoOutput
{
    void Present(VideoFrame frame);
}

/// <summary>An owned immutable frame. It remains valid after subsequent emulation advances.</summary>
public sealed class VideoFrame
{
    private readonly byte[] pixels;
    internal VideoFrame(VideoState video, long number, long cycles, bool startup)
    {
        this.pixels = new byte[Video.Width * Video.Height];
        DmgScreenRenderer.RenderScreen(video, this.pixels);
        this.Number = number; this.Cycles = cycles; this.IsStartup = startup;
    }
    public const int Width = Video.Width;
    public const int Height = Video.Height;
    public long Number { get; }
    public long Cycles { get; }
    public bool IsStartup { get; }
    public ReadOnlySpan<byte> Pixels => this.pixels;
}

public enum EmulationState { Empty, Paused, Running, Faulted, Disposed }
public enum PauseReason { Inactive, Menu, Dialog, Minimized, Resizing }

public sealed record EmulationStatistics(long Frames, long Cycles, double FramesPerSecond, double Speed);

public sealed class EmulationHostOptions
{
    public int MaximumCatchUpFrames { get; init; } = 2;
    public int MaximumInstructionsPerAdvance { get; init; } = 32768;
    public TimeSpan MaximumAdvanceDuration { get; init; } = TimeSpan.FromMilliseconds(8);
    public bool CaptureFrames { get; init; } = true;
    public bool AllowUnsupportedCartridges { get; init; }
    public int HistoryCapacity { get; init; } = 200;
}
