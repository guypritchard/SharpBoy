using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using GB.Emulator.Core;

namespace GB.Debugger;

/// <summary>Live bus activity for the exploded Game Boy teaching view.</summary>
internal sealed partial class InteractionDiagramControl : Control
{
    private const float TraceAccessPulse = 0.004f;
    private const float StepAccessPulse = 0.16f;
    private const float GlowHalfLifeMs = 150f;

    private readonly Traffic[] activity = Enumerable.Range(0, Enum.GetValues<HardwareNode>().Length)
        .Select(_ => new Traffic()).ToArray();
    private readonly Heat[] heat = Enumerable.Range(0, Enum.GetValues<HardwareNode>().Length)
        .Select(_ => new Heat()).ToArray();
    private readonly System.Windows.Forms.Timer decayTimer = new() { Interval = 33 };
    private long lastDecayTick = Stopwatch.GetTimestamp();
    private CpuStepResult? step;
    private ushort pc;
    private byte scanline;
    private byte previousScanline;
    private bool hasRom;

    public InteractionDiagramControl()
    {
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.BackColor = PosterBackground;
        this.Size = BoardSize;
        this.decayTimer.Tick += (_, _) =>
        {
            this.DecayHeat();
            this.Invalidate();
        };
    }

    public void ShowStep(
        CpuStepResult? result,
        ushort programCounter,
        byte currentScanline,
        byte scanlineBeforeStep,
        bool romLoaded,
        IEnumerable<ushort> readAddresses,
        IEnumerable<ushort> writtenAddresses,
        bool tracing = false)
    {
        this.step = result;
        this.pc = programCounter;
        this.scanline = currentScanline;
        this.previousScanline = scanlineBeforeStep;
        this.hasRom = romLoaded;
        foreach (Traffic traffic in this.activity) traffic.Clear();
        foreach (ushort address in readAddresses) this.activity[(int)Classify(address)].Reads++;
        foreach (ushort address in writtenAddresses) this.activity[(int)Classify(address)].Writes++;
        if (result != null) this.activity[(int)Classify(result.Address)].Fetches++;
        if (!tracing && result != null) this.RecordActivity(result, readAddresses, writtenAddresses, StepAccessPulse);
        this.Invalidate();
    }

    public void RecordTraceStep(CpuStepResult result, IEnumerable<ushort> reads, IEnumerable<ushort> writes) =>
        this.RecordActivity(result, reads, writes, TraceAccessPulse);

    public void ClearHistory()
    {
        this.decayTimer.Stop();
        foreach (Heat node in this.heat) node.Clear();
        this.lastDecayTick = Stopwatch.GetTimestamp();
        this.Invalidate();
    }

    private void RecordActivity(CpuStepResult result, IEnumerable<ushort> reads,
        IEnumerable<ushort> writes, float amount)
    {
        foreach (ushort address in reads)
        {
            Heat node = this.heat[(int)Classify(address)];
            node.Read = Math.Min(1, node.Read + amount);
        }
        foreach (ushort address in writes)
        {
            Heat node = this.heat[(int)Classify(address)];
            node.Write = Math.Min(1, node.Write + amount);
        }
        Heat fetch = this.heat[(int)Classify(result.Address)];
        fetch.Read = Math.Min(1, fetch.Read + amount);
        if (!this.decayTimer.Enabled)
        {
            this.lastDecayTick = Stopwatch.GetTimestamp();
            this.decayTimer.Start();
        }
    }

    private void DecayHeat()
    {
        long now = Stopwatch.GetTimestamp();
        float elapsedMs = (float)Stopwatch.GetElapsedTime(this.lastDecayTick, now).TotalMilliseconds;
        this.lastDecayTick = now;
        float decay = MathF.Pow(0.5f, elapsedMs / GlowHalfLifeMs);
        bool glowing = false;
        foreach (Heat node in this.heat)
        {
            node.Read *= decay;
            node.Write *= decay;
            if (node.Read < 0.0005f) node.Read = 0;
            if (node.Write < 0.0005f) node.Write = 0;
            glowing |= node.Read > 0 || node.Write > 0;
        }
        if (!glowing) this.decayTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.decayTimer.Dispose();
        base.Dispose(disposing);
    }

    public static bool IsVideoAddress(ushort address) =>
        address == 0xFF46 || Classify(address) is HardwareNode.VideoRam or HardwareNode.Oam or HardwareNode.Lcd;

    public static bool IsSoundAddress(ushort address) => Classify(address) == HardwareNode.Apu;

    private static HardwareNode Classify(ushort address) => address switch
    {
        <= 0x7FFF => HardwareNode.CartridgeRom,
        <= 0x9FFF => HardwareNode.VideoRam,
        <= 0xBFFF => HardwareNode.CartridgeRam,
        <= 0xCFFF => HardwareNode.WorkRam0,
        <= 0xDFFF => HardwareNode.WorkRam1,
        <= 0xEFFF => HardwareNode.WorkRam0, // Echo address range.
        <= 0xFDFF => HardwareNode.WorkRam1,
        <= 0xFE9F => HardwareNode.Oam,
        0xFF00 => HardwareNode.Joypad,
        0xFF01 or 0xFF02 => HardwareNode.Serial,
        0xFF0F or 0xFFFF => HardwareNode.Interrupt,
        >= 0xFF10 and <= 0xFF3F => HardwareNode.Apu,
        0xFF46 => HardwareNode.Other,
        >= 0xFF40 and <= 0xFF4B => HardwareNode.Lcd,
        >= 0xFF80 and <= 0xFFFE => HardwareNode.HighRam,
        >= 0xFF00 and <= 0xFF7F => HardwareNode.Io,
        _ => HardwareNode.Other
    };

    private static Color SignalFor(Heat heat) => heat.Read > 0.0005f && heat.Write > 0.0005f
        ? BothColor : heat.Write > 0.0005f ? WriteColor : heat.Read > 0.0005f ? ReadColor : Copper;

    private static void DrawTrace(Graphics g, Color color, float strength, params PointF[] points)
    {
        using var copper = new Pen(Copper, 2) { LineJoin = LineJoin.Round };
        g.DrawLines(copper, points);
        if (strength > 0.02f)
        {
            using var outerGlow = new Pen(Color.FromArgb((int)(55 * strength), color), 21)
                { LineJoin = LineJoin.Round };
            using var innerGlow = new Pen(Color.FromArgb((int)(120 * strength), color), 10)
                { LineJoin = LineJoin.Round };
            using var signal = new Pen(Color.FromArgb((int)(240 * strength), color), 3)
                { LineJoin = LineJoin.Round };
            g.DrawLines(outerGlow, points);
            g.DrawLines(innerGlow, points);
            g.DrawLines(signal, points);
        }
    }

    private static Color ColorBlend(Color idle, Color active, float strength) => Color.FromArgb(
        (int)(idle.R + (active.R - idle.R) * strength),
        (int)(idle.G + (active.G - idle.G) * strength),
        (int)(idle.B + (active.B - idle.B) * strength));

    private enum HardwareNode
    {
        CartridgeRom, CartridgeRam, WorkRam0, WorkRam1, VideoRam, Oam, Lcd, Apu,
        Joypad, Serial, Io, HighRam, Interrupt, Other
    }

    private readonly record struct ChipSpec(HardwareNode Node, string Title, string Address,
        string Note, RectangleF Bounds, Color Accent);

    private sealed class Traffic
    {
        public int Reads;
        public int Writes;
        public int Fetches;
        public bool Active => this.Reads + this.Writes + this.Fetches > 0;
        public void Clear() => this.Reads = this.Writes = this.Fetches = 0;
    }

    private sealed class Heat
    {
        public float Read;
        public float Write;
        public float Strength => MathF.Pow(Math.Max(this.Read, this.Write), 0.4f);
        public void Clear() => this.Read = this.Write = 0;
    }
}
