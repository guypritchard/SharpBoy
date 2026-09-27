using System.Drawing.Drawing2D;
using GB.Emulator.Core;

namespace GB.Debugger;

internal sealed class InteractionDiagramControl : Control
{
    private const float CanvasWidth = 740;
    private const float CanvasHeight = 390;
    private static readonly Color Surface = Color.FromArgb(15, 23, 42);
    private static readonly Color Card = Color.FromArgb(30, 41, 59);
    private static readonly Color Muted = Color.FromArgb(148, 163, 184);
    private static readonly Color ReadColor = Color.FromArgb(56, 189, 248);
    private static readonly Color WriteColor = Color.FromArgb(251, 191, 36);
    private static readonly Color VideoColor = Color.FromArgb(74, 222, 128);
    private static readonly Color SoundColor = Color.FromArgb(192, 132, 252);

    private CpuStepResult? step;
    private ushort pc;
    private byte scanline;
    private byte previousScanline;
    private bool hasRom;
    private int reads;
    private int writes;
    private int videoReads;
    private int videoWrites;
    private int soundReads;
    private int soundWrites;

    public InteractionDiagramControl()
    {
        this.DoubleBuffered = true;
        this.BackColor = Surface;
        this.MinimumSize = new Size(360, 220);
    }

    public void ShowStep(
        CpuStepResult? result,
        ushort programCounter,
        byte currentScanline,
        byte scanlineBeforeStep,
        bool romLoaded,
        IEnumerable<ushort> readAddresses,
        IEnumerable<ushort> writtenAddresses)
    {
        this.step = result;
        this.pc = programCounter;
        this.scanline = currentScanline;
        this.previousScanline = scanlineBeforeStep;
        this.hasRom = romLoaded;
        this.reads = this.writes = 0;
        this.videoReads = this.videoWrites = 0;
        this.soundReads = this.soundWrites = 0;

        foreach (ushort address in readAddresses)
        {
            this.reads++;
            if (IsVideoAddress(address)) this.videoReads++;
            if (IsSoundAddress(address)) this.soundReads++;
        }

        foreach (ushort address in writtenAddresses)
        {
            this.writes++;
            if (IsVideoAddress(address)) this.videoWrites++;
            if (IsSoundAddress(address)) this.soundWrites++;
        }

        this.Invalidate();
    }

    public static bool IsVideoAddress(ushort address) =>
        address is >= 0x8000 and <= 0x9FFF or >= 0xFE00 and <= 0xFE9F or >= 0xFF40 and <= 0xFF4B;

    public static bool IsSoundAddress(ushort address) => address is >= 0xFF10 and <= 0xFF3F;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Surface);

        float scale = Math.Min(this.ClientSize.Width / CanvasWidth, this.ClientSize.Height / CanvasHeight);
        if (scale <= 0) return;
        g.TranslateTransform((this.ClientSize.Width - CanvasWidth * scale) / 2,
            (this.ClientSize.Height - CanvasHeight * scale) / 2);
        g.ScaleTransform(scale, scale);

        using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
        using var bodyFont = new Font("Segoe UI", 10);
        using var smallFont = new Font("Segoe UI", 9);
        using var textBrush = new SolidBrush(Color.FromArgb(241, 245, 249));
        using var mutedBrush = new SolidBrush(Muted);
        using var readBrush = new SolidBrush(ReadColor);
        using var writeBrush = new SolidBrush(WriteColor);

        g.DrawString("Instruction flow", titleFont, textBrush, 24, 14);
        g.DrawString(this.step == null ? "Load a ROM and press Step" :
            $"0x{this.step.Address:X4} · {this.step.Instruction.Name}", bodyFont, mutedBrush, 24, 46);

        DrawArrow(g, new PointF(285, 129), new PointF(218, 129),
            this.step != null, ReadColor);
        DrawArrow(g, new PointF(285, 190), new PointF(218, 190),
            this.reads > 0, ReadColor);
        DrawArrow(g, new PointF(218, 218), new PointF(285, 218),
            this.writes > 0, WriteColor);
        DrawArrow(g, new PointF(465, 133), new PointF(525, 105), this.videoWrites > 0, VideoColor);
        DrawArrow(g, new PointF(525, 129), new PointF(465, 158), this.videoReads > 0, VideoColor);
        DrawArrow(g, new PointF(465, 205), new PointF(525, 276), this.soundWrites > 0, SoundColor);
        DrawArrow(g, new PointF(525, 300), new PointF(465, 229), this.soundReads > 0, SoundColor);

        DrawCard(g, new RectangleF(24, 94, 194, 158), "CPU", ReadColor,
            this.hasRom ? $"PC  0x{this.pc:X4}" : "Waiting for ROM",
            this.step == null ? "Execute one instruction" : "Instruction complete");
        DrawCard(g, new RectangleF(285, 94, 180, 158), "MEMORY", WriteColor,
            $"Reads   {this.reads}", $"Writes  {this.writes}");
        DrawCard(g, new RectangleF(525, 70, 190, 137), "VIDEO", VideoColor,
            this.hasRom ? $"Scanline  {this.scanline}/153" : "LCD timing",
            $"R {this.videoReads}  ·  W {this.videoWrites}" +
            (this.scanline != this.previousScanline ? " · LY moved" : ""));
        DrawCard(g, new RectangleF(525, 253, 190, 112), "SOUND", SoundColor,
            $"R {this.soundReads}  ·  W {this.soundWrites}", "Registers only; no APU");

        g.DrawString("FETCH", smallFont, readBrush, 229, 108);
        g.DrawString("READ", smallFont, readBrush, 231, 169);
        g.DrawString("WRITE", smallFont, writeBrush, 229, 224);
        g.DrawString("VRAM · OAM · LCD", smallFont, mutedBrush, 482, 80);
        g.DrawString("0xFF10–0xFF3F", smallFont, mutedBrush, 487, 333);
        g.DrawString("Colored arrows show the last instruction. The CPU clocks LCD timing on every step.",
            smallFont, mutedBrush, 24, 354);
    }

    private static void DrawCard(Graphics g, RectangleF bounds, string title, Color accent,
        string firstLine, string secondLine)
    {
        using var fill = new SolidBrush(Card);
        using var border = new Pen(accent, 2);
        using var titleBrush = new SolidBrush(accent);
        using var bodyBrush = new SolidBrush(Color.FromArgb(241, 245, 249));
        using var noteBrush = new SolidBrush(Muted);
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Bold);
        using var bodyFont = new Font("Consolas", 10);
        using var noteFont = new Font("Segoe UI", 9);
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(bounds, new SizeF(12, 12));
        g.FillPath(fill, path);
        g.DrawPath(border, path);
        g.DrawString(title, titleFont, titleBrush, bounds.X + 14, bounds.Y + 12);
        g.DrawString(firstLine, bodyFont, bodyBrush, bounds.X + 14, bounds.Y + 55);
        g.DrawString(secondLine, noteFont, noteBrush, bounds.X + 14, bounds.Y + 82);
    }

    private static void DrawArrow(Graphics g, PointF start, PointF end, bool active, Color color)
    {
        using var pen = new Pen(active ? color : Color.FromArgb(71, 85, 105), active ? 3 : 2);
        pen.CustomEndCap = new AdjustableArrowCap(5, 6);
        if (!active) pen.DashStyle = DashStyle.Dash;
        g.DrawLine(pen, start, end);
    }
}
