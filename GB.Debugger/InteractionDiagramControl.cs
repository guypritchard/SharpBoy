using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Diagnostics;
using GB.Emulator.Core;

namespace GB.Debugger;

/// <summary>A silkscreen-style view of the mapped Game Boy hardware bus.</summary>
internal sealed class InteractionDiagramControl : Control
{
    public static readonly Size BoardSize = new(1040, 840);

    private const float LeftBusX = 251;
    private const float RightBusX = 785;
    private const float CpuBusY = 407;
    private const float BottomBusY = 635;
    private const float TraceAccessPulse = 0.004f;
    private const float StepAccessPulse = 0.16f;
    private const float GlowHalfLifeMs = 150f;
    private static readonly RectangleF CpuBounds = new(425, 300, 190, 215);
    private static readonly Color BoardGreen = Color.FromArgb(20, 53, 44);
    private static readonly Color BoardEdge = Color.FromArgb(48, 108, 81);
    private static readonly Color ChipBlack = Color.FromArgb(23, 31, 29);
    private static readonly Color ChipInset = Color.FromArgb(39, 48, 43);
    private static readonly Color Silk = Color.FromArgb(230, 236, 211);
    private static readonly Color Muted = Color.FromArgb(157, 184, 159);
    private static readonly Color Copper = Color.FromArgb(102, 120, 74);
    private static readonly Color Gold = Color.FromArgb(194, 160, 89);
    private static readonly Color ReadColor = Color.FromArgb(91, 213, 230);
    private static readonly Color WriteColor = Color.FromArgb(255, 189, 93);
    private static readonly Color BothColor = Color.FromArgb(224, 145, 219);
    private static readonly Color ClockColor = Color.FromArgb(134, 214, 139);
    private static readonly Color ButtonColor = Color.FromArgb(155, 75, 123);

    private static readonly ChipSpec[] Chips =
    {
        new(HardwareNode.CartridgeRom, "CARTRIDGE ROM", "0000–7FFF", new RectangleF(52, 140, 185, 78), ChipSide.Left),
        new(HardwareNode.CartridgeRam, "CARTRIDGE RAM", "A000–BFFF", new RectangleF(52, 275, 185, 78), ChipSide.Left),
        new(HardwareNode.WorkRam0, "WORK RAM 0", "C000–CFFF", new RectangleF(52, 410, 185, 78), ChipSide.Left),
        new(HardwareNode.WorkRam1, "WORK RAM 1", "D000–DFFF", new RectangleF(52, 545, 185, 78), ChipSide.Left),
        new(HardwareNode.Io, "TIMER / I/O", "FF03–FF7F", new RectangleF(270, 315, 130, 78), ChipSide.InnerLeft),
        new(HardwareNode.Interrupt, "INTERRUPTS", "FF0F / FFFF", new RectangleF(270, 435, 130, 78), ChipSide.InnerLeft),
        new(HardwareNode.VideoRam, "VIDEO RAM", "8000–9FFF · tiles", new RectangleF(805, 140, 185, 78), ChipSide.Right),
        new(HardwareNode.Oam, "SPRITE OAM", "FE00–FE9F", new RectangleF(805, 275, 185, 78), ChipSide.Right),
        new(HardwareNode.Lcd, "LCD / PPU", "FF40–FF4B", new RectangleF(805, 410, 185, 78), ChipSide.Right),
        new(HardwareNode.Apu, "APU / SOUND", "FF10–FF3F", new RectangleF(805, 545, 185, 78), ChipSide.Right),
        new(HardwareNode.Joypad, "JOYPAD", "FF00", new RectangleF(60, 680, 190, 78), ChipSide.Bottom),
        new(HardwareNode.Serial, "SERIAL LINK", "FF01–FF02", new RectangleF(295, 680, 190, 78), ChipSide.Bottom),
        new(HardwareNode.HighRam, "HIGH RAM", "FF80–FFFE", new RectangleF(555, 680, 190, 78), ChipSide.Bottom),
        new(HardwareNode.Other, "OAM DMA / BUS", "FF46 / misc", new RectangleF(790, 680, 190, 78), ChipSide.Bottom)
    };

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
        this.BackColor = BoardGreen;
        this.Size = BoardSize;
        this.MinimumSize = BoardSize;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        this.DrawBoard(g);
        this.DrawTraces(g);

        using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subtitleFont = new Font("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var chipTitleFont = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        using var chipDetailFont = new Font("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var cpuTitleFont = new Font("Segoe UI", 19, FontStyle.Bold, GraphicsUnit.Pixel);
        using var cpuDetailFont = new Font("Consolas", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var silkBrush = new SolidBrush(Silk);
        using var mutedBrush = new SolidBrush(Muted);

        DrawText(g, "GAME BOY  /  DMG-01", titleFont, silkBrush, new RectangleF(80, 31, 650, 32));
        DrawText(g, "DOT MATRIX  •  HARDWARE BUS  •  LIVE SIGNALS", subtitleFont,
            mutedBrush, new RectangleF(82, 68, 670, 20));
        DrawStatusBadge(g, subtitleFont);

        foreach (ChipSpec chip in Chips)
            this.DrawChip(g, chip, chipTitleFont, chipDetailFont);
        this.DrawCpu(g, cpuTitleFont, cpuDetailFont);
        this.DrawLegend(g, subtitleFont);
    }

    private void DrawBoard(Graphics g)
    {
        g.Clear(BoardGreen);
        using var edge = new Pen(BoardEdge, 3);
        using var dotBrush = new SolidBrush(Color.FromArgb(37, 91, 69));
        using var holeOuter = new SolidBrush(Gold);
        using var holeInner = new SolidBrush(Color.FromArgb(9, 33, 29));
        g.DrawRoundedRectangle(edge, new Rectangle(12, 12, 1016, 816), new Size(26, 26));
        using var frame = new Pen(Color.FromArgb(107, 139, 109), 2);
        g.DrawRoundedRectangle(frame, new Rectangle(22, 22, 996, 796), new Size(20, 20));
        for (int y = 110; y < 775; y += 25)
            for (int x = 35; x < 1015; x += 25)
                g.FillEllipse(dotBrush, x, y, 2, 2);
        foreach (Point hole in new[] { new Point(38, 38), new Point(1002, 38),
                     new Point(38, 802), new Point(1002, 802) })
        {
            g.FillEllipse(holeOuter, hole.X - 8, hole.Y - 8, 16, 16);
            g.FillEllipse(holeInner, hole.X - 4, hole.Y - 4, 8, 8);
        }
        using var separator = new Pen(Color.FromArgb(63, 130, 91), 1);
        g.DrawLine(separator, 50, 780, 990, 780);
        using var button = new SolidBrush(ButtonColor);
        g.FillEllipse(button, 701, 39, 15, 15);
        g.FillEllipse(button, 726, 39, 15, 15);
        using var pad = new SolidBrush(Color.FromArgb(30, 39, 36));
        g.FillRectangle(pad, 652, 34, 12, 31);
        g.FillRectangle(pad, 642, 44, 32, 11);
        using var grille = new Pen(BoardEdge, 3);
        for (int i = 0; i < 5; i++)
            g.DrawLine(grille, 894 + i * 17, 99, 903 + i * 17, 89);
    }

    private void DrawTraces(Graphics g)
    {
        DrawTrace(g, SignalFor(ChipSide.Left), StrengthFor(ChipSide.Left),
            new PointF(LeftBusX, 179), new PointF(LeftBusX, 584));
        DrawTrace(g, SignalFor(ChipSide.Right), StrengthFor(ChipSide.Right),
            new PointF(RightBusX, 179), new PointF(RightBusX, 584));
        DrawTrace(g, SignalFor(ChipSide.Left), StrengthFor(ChipSide.Left),
            new PointF(LeftBusX, CpuBusY), new PointF(CpuBounds.Left, CpuBusY));
        DrawTrace(g, SignalFor(ChipSide.Right), StrengthFor(ChipSide.Right),
            new PointF(CpuBounds.Right, CpuBusY), new PointF(RightBusX, CpuBusY));
        DrawTrace(g, SignalFor(ChipSide.Bottom), StrengthFor(ChipSide.Bottom),
            new PointF(520, CpuBounds.Bottom), new PointF(520, BottomBusY));
        DrawTrace(g, SignalFor(ChipSide.Bottom), StrengthFor(ChipSide.Bottom),
            new PointF(155, BottomBusY), new PointF(885, BottomBusY));

        foreach (ChipSpec chip in Chips)
        {
            Heat heat = this.heat[(int)chip.Node];
            Color signal = SignalFor(heat);
            float strength = heat.Strength;
            float centerY = chip.Bounds.Top + chip.Bounds.Height / 2;
            float centerX = chip.Bounds.Left + chip.Bounds.Width / 2;
            switch (chip.Side)
            {
                case ChipSide.Left:
                    DrawTrace(g, signal, strength,
                        new PointF(chip.Bounds.Right, centerY), new PointF(LeftBusX, centerY));
                    DrawVia(g, new PointF(LeftBusX, centerY), signal, strength);
                    break;
                case ChipSide.Right:
                    DrawTrace(g, signal, strength,
                        new PointF(RightBusX, centerY), new PointF(chip.Bounds.Left, centerY));
                    DrawVia(g, new PointF(RightBusX, centerY), signal, strength);
                    break;
                case ChipSide.InnerLeft:
                    DrawTrace(g, signal, strength,
                        new PointF(chip.Bounds.Right, centerY), new PointF(CpuBounds.Left, centerY));
                    break;
                case ChipSide.Bottom:
                    DrawTrace(g, signal, strength,
                        new PointF(centerX, BottomBusY), new PointF(centerX, chip.Bounds.Top));
                    DrawVia(g, new PointF(centerX, BottomBusY), signal, strength);
                    break;
            }
        }
    }

    private float StrengthFor(ChipSide side) => MathF.Pow(Math.Min(1f,
        Chips.Where(chip => chip.Side == side)
            .Sum(chip => this.heat[(int)chip.Node].Read + this.heat[(int)chip.Node].Write)), 0.4f);

    private Color SignalFor(ChipSide side)
    {
        bool reads = Chips.Any(chip => chip.Side == side && this.heat[(int)chip.Node].Read > 0.0005f);
        bool writes = Chips.Any(chip => chip.Side == side && this.heat[(int)chip.Node].Write > 0.0005f);
        return reads && writes ? BothColor : writes ? WriteColor : reads ? ReadColor : Copper;
    }

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

    private static void DrawVia(Graphics g, PointF point, Color color, float strength)
    {
        using var rim = new SolidBrush(ColorBlend(Gold, color, strength));
        using var center = new SolidBrush(BoardGreen);
        g.FillEllipse(rim, point.X - 4, point.Y - 4, 8, 8);
        g.FillEllipse(center, point.X - 2, point.Y - 2, 4, 4);
    }

    private void DrawChip(Graphics g, ChipSpec chip, Font titleFont, Font detailFont)
    {
        Traffic traffic = this.activity[(int)chip.Node];
        Heat heat = this.heat[(int)chip.Node];
        Color signal = SignalFor(heat);
        RectangleF b = chip.Bounds;
        using var fill = new SolidBrush(ChipBlack);
        using var inset = new Pen(ChipInset, 1);
        using var border = new Pen(ColorBlend(Gold, signal, heat.Strength), heat.Strength > 0.015f ? 2.5f : 1.5f);
        using var titleBrush = new SolidBrush(ColorBlend(Silk, signal, heat.Strength));
        using var detailBrush = new SolidBrush(Muted);
        using var activityBrush = new SolidBrush(traffic.Active ? Silk : Muted);
        DrawPins(g, b, 5);
        g.FillRectangle(fill, b);
        g.DrawRectangle(border, b.X, b.Y, b.Width, b.Height);
        g.DrawRectangle(inset, b.X + 4, b.Y + 4, b.Width - 8, b.Height - 8);
        g.FillEllipse(detailBrush, b.X + 7, b.Y + 7, 4, 4);
        DrawText(g, chip.Title, titleFont, titleBrush,
            new RectangleF(b.X + 12, b.Y + 9, b.Width - 24, 22));
        DrawText(g, chip.Address, detailFont, detailBrush,
            new RectangleF(b.X + 12, b.Y + 34, b.Width - 24, 16));
        string status = chip.Node == HardwareNode.Lcd
            ? $"LY {this.scanline:000}  R{traffic.Reads} W{traffic.Writes}"
            : traffic.Fetches > 0
                ? $"FETCH {traffic.Fetches}  R{traffic.Reads} W{traffic.Writes}"
                : $"R{traffic.Reads}  W{traffic.Writes}";
        DrawText(g, status, detailFont, activityBrush,
            new RectangleF(b.X + 12, b.Y + 56, b.Width - 24, 17));
    }

    private void DrawCpu(Graphics g, Font titleFont, Font detailFont)
    {
        RectangleF b = CpuBounds;
        using var fill = new SolidBrush(ChipBlack);
        using var edge = new Pen(this.step == null ? Gold : ClockColor, 3);
        using var inset = new Pen(ChipInset, 1);
        using var titleBrush = new SolidBrush(Silk);
        using var detailBrush = new SolidBrush(Muted);
        using var activeBrush = new SolidBrush(ClockColor);
        DrawPins(g, b, 12);
        g.FillRectangle(fill, b);
        g.DrawRectangle(edge, b.X, b.Y, b.Width, b.Height);
        g.DrawRectangle(inset, b.X + 7, b.Y + 7, b.Width - 14, b.Height - 14);
        g.FillEllipse(detailBrush, b.X + 12, b.Y + 11, 8, 8);
        DrawText(g, "DMG CPU", titleFont, titleBrush, new RectangleF(b.X + 23, b.Y + 28, b.Width - 46, 30));
        DrawText(g, "LR35902  /  8 BIT", detailFont, detailBrush,
            new RectangleF(b.X + 23, b.Y + 62, b.Width - 46, 22));
        using var rule = new Pen(Color.FromArgb(95, 111, 91), 1);
        g.DrawLine(rule, b.X + 20, b.Y + 95, b.Right - 20, b.Y + 95);
        DrawText(g, this.hasRom ? $"PC  0x{this.pc:X4}" : "NO CARTRIDGE", detailFont,
            titleBrush, new RectangleF(b.X + 20, b.Y + 108, b.Width - 40, 22));
        DrawText(g, this.step == null ? "STEP TO TRACE" : this.step.Instruction.Name,
            detailFont, this.step == null ? detailBrush : activeBrush,
            new RectangleF(b.X + 20, b.Y + 139, b.Width - 40, 23));
        DrawText(g, this.scanline != this.previousScanline ? "LY ADVANCED" : "LCD CLOCKING",
            detailFont, detailBrush, new RectangleF(b.X + 20, b.Y + 175, b.Width - 40, 20));
    }

    private void DrawStatusBadge(Graphics g, Font font)
    {
        RectangleF badge = new(770, 32, 220, 35);
        using var fill = new SolidBrush(Color.FromArgb(31, 75, 55));
        using var border = new Pen(BoardEdge, 1);
        using var brush = new SolidBrush(this.step == null ? Muted : ClockColor);
        g.FillRectangle(fill, badge);
        g.DrawRectangle(border, badge.X, badge.Y, badge.Width, badge.Height);
        DrawText(g, this.step == null ? "READY  /  PRESS STEP" : $"LAST STEP  /  {this.step.Address:X4}",
            font, brush, new RectangleF(badge.X + 12, badge.Y + 7, badge.Width - 24, 20));
    }

    private void DrawLegend(Graphics g, Font font)
    {
        DrawLegendItem(g, font, 65, ReadColor, "READ / FETCH");
        DrawLegendItem(g, font, 250, WriteColor, "WRITE");
        DrawLegendItem(g, font, 370, BothColor, "READ + WRITE");
        DrawLegendItem(g, font, 560, ClockColor, "CPU / LCD CLOCK");
        using var brush = new SolidBrush(Muted);
        DrawText(g, "TRAIL FADES WITH TIME", font, brush, new RectangleF(790, 793, 205, 22));
    }

    private static void DrawLegendItem(Graphics g, Font font, float x, Color color, string label)
    {
        using var dot = new SolidBrush(color);
        using var text = new SolidBrush(Silk);
        g.FillEllipse(dot, x, 797, 9, 9);
        DrawText(g, label, font, text, new RectangleF(x + 16, 789, 170, 24));
    }

    private static void DrawPins(Graphics g, RectangleF bounds, int count)
    {
        using var pen = new Pen(Gold, 3);
        float spacing = (bounds.Height - 20) / (count - 1);
        for (int i = 0; i < count; i++)
        {
            float y = bounds.Y + 10 + i * spacing;
            g.DrawLine(pen, bounds.Left - 6, y, bounds.Left, y);
            g.DrawLine(pen, bounds.Right, y, bounds.Right + 6, y);
        }
    }

    private static void DrawText(Graphics g, string text, Font font, Brush brush, RectangleF bounds)
    {
        using var format = new StringFormat
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, brush, bounds, format);
    }

    private enum ChipSide { Left, InnerLeft, Right, Bottom }
    private enum HardwareNode
    {
        CartridgeRom, CartridgeRam, WorkRam0, WorkRam1, VideoRam, Oam, Lcd, Apu,
        Joypad, Serial, Io, HighRam, Interrupt, Other
    }

    private readonly record struct ChipSpec(HardwareNode Node, string Title, string Address,
        RectangleF Bounds, ChipSide Side);

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
