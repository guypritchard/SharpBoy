using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GB.Emulator.Core;

namespace GB.Debugger;

/// <summary>A complete address-space view with activity positioned within each hardware range.</summary>
internal sealed class MemoryMapControl : Control
{
    private const int RowHeight = 47;
    private const int FirstRowY = 119;
    private const int LaneX = 598;
    private const int LaneWidth = 400;
    private const int Buckets = 48;
    private const float HalfLifeMs = 420f;
    private const float TracePulse = 0.045f;
    private const float StepPulse = 0.55f;
    private static readonly Color Background = Color.FromArgb(17, 26, 43);
    private static readonly Color Foreground = Color.FromArgb(239, 244, 240);
    private static readonly Color Dim = Color.FromArgb(160, 179, 194);
    private static readonly Color Read = Color.FromArgb(78, 220, 244);
    private static readonly Color Write = Color.FromArgb(255, 177, 83);
    private static readonly Color Fetch = Color.FromArgb(247, 243, 200);
    private static readonly AddressBlock[] Regions =
    {
        new(0x0000, 0x3FFF, "ROM BANK 00", "Fixed cartridge code", Color.FromArgb(242, 122, 118)),
        new(0x4000, 0x7FFF, "ROM BANK 01–", "Switchable cartridge code", Color.FromArgb(242, 122, 118)),
        new(0x8000, 0x9FFF, "VIDEO RAM", "Tiles and tile maps · PPU", Color.FromArgb(169, 219, 98)),
        new(0xA000, 0xBFFF, "CARTRIDGE RAM", "Game save / external RAM", Color.FromArgb(240, 151, 111)),
        new(0xC000, 0xCFFF, "WORK RAM 0", "Main system memory", Color.FromArgb(182, 160, 249)),
        new(0xD000, 0xDFFF, "WORK RAM 1", "Second work RAM bank", Color.FromArgb(182, 160, 249)),
        new(0xE000, 0xFDFF, "ECHO RAM", "Mirror of C000–DDFF", Color.FromArgb(149, 137, 207)),
        new(0xFE00, 0xFE9F, "SPRITE OAM", "Sprite positions · PPU", Color.FromArgb(169, 219, 98)),
        new(0xFEA0, 0xFEFF, "UNUSABLE", "No hardware responds", Color.FromArgb(112, 128, 145)),
        new(0xFF00, 0xFF00, "JOYPAD", "Button input", Color.FromArgb(239, 122, 183)),
        new(0xFF01, 0xFF02, "SERIAL", "Link cable data + control", Color.FromArgb(79, 206, 225)),
        new(0xFF03, 0xFF03, "UNUSED I/O", "Unassigned register", Color.FromArgb(112, 128, 145)),
        new(0xFF04, 0xFF07, "TIMER", "Divider, counter, modulo, control", Color.FromArgb(252, 202, 97)),
        new(0xFF08, 0xFF0E, "UNUSED I/O", "Unassigned registers", Color.FromArgb(112, 128, 145)),
        new(0xFF0F, 0xFF0F, "INTERRUPT IF", "Interrupt requests", Color.FromArgb(249, 214, 122)),
        new(0xFF10, 0xFF3F, "SOUND / APU", "Four channels + wave RAM", Color.FromArgb(255, 145, 116)),
        new(0xFF40, 0xFF45, "LCD / PPU", "Display status and scroll", Color.FromArgb(169, 219, 98)),
        new(0xFF46, 0xFF46, "OAM DMA", "Sprite memory transfer", Color.FromArgb(169, 219, 98)),
        new(0xFF47, 0xFF4B, "LCD PALETTES", "Colours and window position", Color.FromArgb(169, 219, 98)),
        new(0xFF4C, 0xFF7F, "OTHER I/O", "Remaining control registers", Color.FromArgb(116, 171, 205)),
        new(0xFF80, 0xFFFE, "HIGH RAM", "Fast CPU workspace", Color.FromArgb(182, 160, 249)),
        new(0xFFFF, 0xFFFF, "INTERRUPT IE", "Interrupt enables", Color.FromArgb(249, 214, 122))
    };

    public static readonly Size MapSize = new(1100, FirstRowY + Regions.Length * RowHeight + 30);

    private readonly RegionActivity[] activity = Regions.Select(_ => new RegionActivity()).ToArray();
    private readonly System.Windows.Forms.Timer decayTimer = new() { Interval = 33 };
    private readonly ToolTip tooltip = new();
    private long lastDecayTick = Stopwatch.GetTimestamp();
    private float zoom = 1;
    private int hoveredRegion = -1;
    private bool hasRom;
    private ushort pc;
    private int selectedRomBank = 1;
    private int? selectedRamBank;

    public MemoryMapControl()
    {
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.BackColor = Background;
        this.Size = MapSize;
        this.decayTimer.Tick += (_, _) => this.Decay();
    }

    public void SetZoom(float value)
    {
        this.zoom = Math.Clamp(value, 0.55f, 1.5f);
        this.Size = new Size((int)(MapSize.Width * this.zoom), (int)(MapSize.Height * this.zoom));
        this.Invalidate();
    }

    public void ShowStep(CpuStepResult? step, ushort programCounter, bool romLoaded,
        IEnumerable<ushort> reads, IEnumerable<ushort> writes, bool tracing,
        int romBank, int? ramBank)
    {
        this.pc = programCounter;
        this.hasRom = romLoaded;
        this.selectedRomBank = romBank;
        this.selectedRamBank = ramBank;
        this.hoveredRegion = -1;
        foreach (RegionActivity state in this.activity) state.ClearCurrent();
        foreach (ushort address in reads) this.MarkCurrent(address, Access.Read);
        foreach (ushort address in writes) this.MarkCurrent(address, Access.Write);
        if (step != null) this.MarkCurrent(step.Address, Access.Fetch);
        if (!tracing && step != null) this.RecordTraceStep(step, reads, writes, StepPulse);
        this.Invalidate();
    }

    public void RecordTraceStep(CpuStepResult step, IEnumerable<ushort> reads,
        IEnumerable<ushort> writes) => this.RecordTraceStep(step, reads, writes, TracePulse);

    public void ClearHistory()
    {
        this.decayTimer.Stop();
        foreach (RegionActivity state in this.activity) state.Clear();
        this.lastDecayTick = Stopwatch.GetTimestamp();
        this.Invalidate();
    }

    private void MarkCurrent(ushort address, Access access)
    {
        int index = FindRegion(address);
        RegionActivity state = this.activity[index];
        int bucket = BucketFor(Regions[index], address);
        switch (access)
        {
            case Access.Read:
                state.CurrentReads[bucket] = true;
                state.CurrentReadAddresses.Add(address);
                break;
            case Access.Write:
                state.CurrentWrites[bucket] = true;
                state.CurrentWriteAddresses.Add(address);
                break;
            case Access.Fetch:
                state.CurrentFetch = bucket;
                state.CurrentFetchAddress = address;
                break;
        }
    }

    private void RecordTraceStep(CpuStepResult step, IEnumerable<ushort> reads,
        IEnumerable<ushort> writes, float pulse)
    {
        foreach (ushort address in reads) this.AddHeat(address, Access.Read, pulse);
        foreach (ushort address in writes) this.AddHeat(address, Access.Write, pulse);
        this.AddHeat(step.Address, Access.Fetch, pulse);
        if (this.decayTimer.Enabled) return;
        this.lastDecayTick = Stopwatch.GetTimestamp();
        this.decayTimer.Start();
    }

    private void AddHeat(ushort address, Access access, float pulse)
    {
        int index = FindRegion(address);
        int bucket = BucketFor(Regions[index], address);
        float[] levels = access == Access.Write ? this.activity[index].WriteHeat : this.activity[index].ReadHeat;
        levels[bucket] = Math.Min(1, levels[bucket] + pulse);
    }

    private void Decay()
    {
        long now = Stopwatch.GetTimestamp();
        float factor = MathF.Pow(0.5f,
            (float)Stopwatch.GetElapsedTime(this.lastDecayTick, now).TotalMilliseconds / HalfLifeMs);
        this.lastDecayTick = now;
        bool glowing = false;
        foreach (RegionActivity state in this.activity)
        {
            foreach (float[] levels in new[] { state.ReadHeat, state.WriteHeat })
                for (int i = 0; i < Buckets; i++)
                {
                    levels[i] *= factor;
                    if (levels[i] < 0.005f) levels[i] = 0;
                    glowing |= levels[i] > 0;
                }
        }
        if (!glowing) this.decayTimer.Stop();
        this.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(Background);
        g.ScaleTransform(this.zoom, this.zoom);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var heading = new Font("Segoe UI", 24, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 11);
        using var rowTitle = new Font("Segoe UI", 12, FontStyle.Bold);
        using var detail = new Font("Segoe UI", 9);
        using var addressFont = new Font("Consolas", 12, FontStyle.Bold);
        using var addressBrush = new SolidBrush(Dim);
        using var textBrush = new SolidBrush(Foreground);
        g.DrawString("THE GAME BOY MEMORY MAP", heading, textBrush, 24, 13);
        g.DrawString("0000 → FFFF  /  CPU address space  /  each line is an exact hardware range",
            subtitle, addressBrush, 27, 57);
        g.DrawString(this.hasRom ? $"PC {this.pc:X4}   ·   cyan READ   ·   amber WRITE   ·   cream FETCH"
            : "Load a ROM and step or trace to see activity within each block.",
            subtitle, textBrush, 27, 85);

        for (int i = 0; i < Regions.Length; i++)
            this.DrawRegion(g, Regions[i], this.activity[i], FirstRowY + i * RowHeight,
                addressFont, rowTitle, detail, addressBrush, textBrush);
    }

    private void DrawRegion(Graphics g, AddressBlock region, RegionActivity state, int y,
        Font addressFont, Font titleFont, Font detailFont, Brush addressBrush, Brush textBrush)
    {
        var bounds = new Rectangle(24, y, 1052, 42);
        using var fill = new SolidBrush(Color.FromArgb(40, region.Color));
        using var accent = new SolidBrush(region.Color);
        using var border = new Pen(Color.FromArgb(94, region.Color));
        g.FillRectangle(fill, bounds);
        g.DrawRectangle(border, bounds);
        g.FillRectangle(accent, 24, y, 5, 42);
        g.DrawString($"{region.Start:X4}–{region.End:X4}", addressFont, addressBrush, 39, y + 9);
        string title = region.Start == 0x4000 ? $"ROM BANK {this.selectedRomBank:X2}" :
            region.Start == 0xA000 && this.selectedRamBank.HasValue
                ? $"CARTRIDGE RAM {this.selectedRamBank.Value:X2}" : region.Name;
        g.DrawString(title, titleFont, textBrush, 202, y + 3);
        g.DrawString(region.Description, detailFont, addressBrush, 204, y + 24);

        using var laneFill = new SolidBrush(Color.FromArgb(31, 43, 60));
        g.FillRectangle(laneFill, LaneX, y + 9, LaneWidth, 24);
        float bucketWidth = (float)LaneWidth / Buckets;
        for (int i = 0; i < Buckets; i++)
        {
            float read = state.ReadHeat[i];
            float write = state.WriteHeat[i];
            float x = LaneX + i * bucketWidth;
            if (read > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb((int)(220 * MathF.Sqrt(read)), Read));
                g.FillRectangle(brush, x, y + 9, bucketWidth + 0.5f, 12);
            }
            if (write > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb((int)(220 * MathF.Sqrt(write)), Write));
                g.FillRectangle(brush, x, y + 21, bucketWidth + 0.5f, 12);
            }
            if (state.CurrentReads[i]) g.FillRectangle(Brushes.Cyan, x, y + 9, Math.Max(2, bucketWidth), 12);
            if (state.CurrentWrites[i]) g.FillRectangle(Brushes.Orange, x, y + 21, Math.Max(2, bucketWidth), 12);
        }
        if (state.CurrentFetch >= 0)
        {
            float x = LaneX + (state.CurrentFetch + 0.5f) * bucketWidth;
            using var fetchPen = new Pen(Fetch, 2);
            g.DrawLine(fetchPen, x, y + 4, x, y + 37);
        }
        if (state.CurrentReadAddresses.Count + state.CurrentWriteAddresses.Count > 0)
        {
            using var countBrush = new SolidBrush(Foreground);
            g.DrawString($"R{state.CurrentReadAddresses.Count} W{state.CurrentWriteAddresses.Count}", detailFont,
                countBrush, 1041, y + 12);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = ((int)(e.Y / this.zoom) - FirstRowY) / RowHeight;
        if (e.Y / this.zoom < FirstRowY || row < 0 || row >= Regions.Length) row = -1;
        if (row == this.hoveredRegion) return;
        this.hoveredRegion = row;
        if (row < 0)
        {
            this.tooltip.SetToolTip(this, string.Empty);
            return;
        }
        RegionActivity state = this.activity[row];
        string reads = state.CurrentReadAddresses.Count == 0 ? "none" :
            string.Join(", ", state.CurrentReadAddresses.Select(address => $"{address:X4}"));
        string writes = state.CurrentWriteAddresses.Count == 0 ? "none" :
            string.Join(", ", state.CurrentWriteAddresses.Select(address => $"{address:X4}"));
        string fetch = state.CurrentFetchAddress is ushort address ? address.ToString("X4") : "none";
        this.tooltip.SetToolTip(this,
            $"{Regions[row].Start:X4}–{Regions[row].End:X4}  {Regions[row].Name}\n" +
            $"{Regions[row].Description}\nRead: {reads}\nWrite: {writes}\nFetch: {fetch}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.decayTimer.Dispose();
            this.tooltip.Dispose();
        }
        base.Dispose(disposing);
    }

    private static int FindRegion(ushort address)
    {
        for (int i = 0; i < Regions.Length; i++)
            if (address <= Regions[i].End) return i;
        throw new ArgumentOutOfRangeException(nameof(address));
    }

    private static int BucketFor(AddressBlock region, ushort address) =>
        Math.Min(Buckets - 1, (address - region.Start) * Buckets / (region.End - region.Start + 1));

    private enum Access { Read, Write, Fetch }

    private sealed record AddressBlock(ushort Start, ushort End, string Name, string Description, Color Color);

    private sealed class RegionActivity
    {
        public readonly float[] ReadHeat = new float[Buckets];
        public readonly float[] WriteHeat = new float[Buckets];
        public readonly bool[] CurrentReads = new bool[Buckets];
        public readonly bool[] CurrentWrites = new bool[Buckets];
        public int CurrentFetch = -1;
        public readonly HashSet<ushort> CurrentReadAddresses = new();
        public readonly HashSet<ushort> CurrentWriteAddresses = new();
        public ushort? CurrentFetchAddress;

        public void ClearCurrent()
        {
            Array.Clear(this.CurrentReads);
            Array.Clear(this.CurrentWrites);
            this.CurrentFetch = -1;
            this.CurrentReadAddresses.Clear();
            this.CurrentWriteAddresses.Clear();
            this.CurrentFetchAddress = null;
        }

        public void Clear()
        {
            this.ClearCurrent();
            Array.Clear(this.ReadHeat);
            Array.Clear(this.WriteHeat);
        }
    }
}
