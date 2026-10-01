using System.Drawing.Drawing2D;
using System.Drawing.Text;
using GB.Emulator.Core.InputOutput;

namespace GB.Debugger;

internal sealed partial class InteractionDiagramControl
{
    public static readonly Size BoardSize = new(1120, 1060);
    public static readonly Color PosterBackground = Color.FromArgb(19, 27, 49);
    private static readonly Color Silk = Color.FromArgb(243, 241, 229);
    private static readonly Color Muted = Color.FromArgb(160, 176, 200);
    private static readonly Color Copper = Color.FromArgb(68, 91, 117);
    private static readonly Color ReadColor = Color.FromArgb(75, 222, 245);
    private static readonly Color WriteColor = Color.FromArgb(255, 180, 72);
    private static readonly Color BothColor = Color.FromArgb(240, 126, 221);
    private static readonly Color Lime = Color.FromArgb(192, 232, 100);
    private static readonly Color Coral = Color.FromArgb(255, 136, 112);
    private static readonly Color Violet = Color.FromArgb(179, 159, 255);
    private static readonly Color Pink = Color.FromArgb(232, 88, 151);
    private float zoom = 1;
    private string heldButtons = "NONE";

    private static readonly ChipSpec[] Chips =
    {
        new(HardwareNode.CartridgeRom, "GAME ROM", "0000–7FFF", "Code + game data", new(425, 183, 145, 86), Coral),
        new(HardwareNode.CartridgeRam, "SAVE RAM", "A000–BFFF", "Cartridge memory", new(585, 183, 145, 86), Coral),
        new(HardwareNode.WorkRam0, "WORK RAM 0", "C000–CFFF", "Working data", new(75, 526, 235, 68), Violet),
        new(HardwareNode.WorkRam1, "WORK RAM 1", "D000–DFFF", "More working data", new(75, 608, 235, 68), Violet),
        new(HardwareNode.HighRam, "HIGH RAM", "FF80–FFFE", "Small, fast workspace", new(75, 690, 235, 68), Violet),
        new(HardwareNode.Io, "TIMER / I/O", "FF03–FF7F", "Timing + registers", new(75, 772, 235, 68), Violet),
        new(HardwareNode.Interrupt, "INTERRUPTS", "FF0F / FFFF", "Devices request attention", new(75, 854, 235, 68), Violet),
        new(HardwareNode.Lcd, "LCD / PPU", "FF40–FF4B", "Builds the 160 × 144 picture", new(430, 419, 300, 68), Lime),
        new(HardwareNode.VideoRam, "VIDEO RAM", "8000–9FFF", "Tiles + maps", new(430, 501, 143, 86), Lime),
        new(HardwareNode.Oam, "SPRITE OAM", "FE00–FE9F", "Sprite positions", new(587, 501, 143, 86), Lime),
        new(HardwareNode.Other, "OAM DMA / BUS", "FF46 / misc", "Copies sprite data", new(430, 601, 300, 68), Lime),
        new(HardwareNode.Joypad, "JOYPAD INPUT", "FF00", "Buttons become input bits", new(430, 853, 300, 68), Pink),
        new(HardwareNode.Serial, "SERIAL LINK", "FF01–FF02", "Bytes to another Game Boy", new(845, 438, 220, 86), ReadColor),
        new(HardwareNode.Apu, "APU / SOUND", "FF10–FF3F", "Sound registers → speaker", new(845, 831, 220, 86), Coral)
    };

    public void SetZoom(float value)
    {
        this.zoom = Math.Clamp(value, 0.25f, 2f);
        this.Size = new Size((int)(BoardSize.Width * this.zoom), (int)(BoardSize.Height * this.zoom));
        this.Invalidate();
    }

    public void ShowButtons(IButtonInput input)
    {
        var held = new List<string>();
        foreach ((GameBoyButton button, string label) in new[]
        {
            (GameBoyButton.Up, "↑"), (GameBoyButton.Down, "↓"),
            (GameBoyButton.Left, "←"), (GameBoyButton.Right, "→"),
            (GameBoyButton.A, "A"), (GameBoyButton.B, "B"),
            (GameBoyButton.Select, "SELECT"), (GameBoyButton.Start, "START")
        })
            if (input.IsPressed(button)) held.Add(label);
        string current = held.Count == 0 ? "NONE" : string.Join(" ", held);
        if (current == this.heldButtons) return;
        this.heldButtons = current;
        this.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.Clear(PosterBackground);
        g.ScaleTransform(this.zoom, this.zoom);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        DrawPoster(g);
        this.DrawConnections(g);
        foreach (ChipSpec chip in Chips) this.DrawChip(g, chip);
        this.DrawCpu(g);
        DrawLabel(g, this.hasRom ? $"PC  {this.pc:X4}   •   LY  {this.scanline:000}" : "LOAD A CARTRIDGE TO BEGIN",
            13, ReadColor, new(785, 65, 295, 24), true);
    }

    private static void DrawPoster(Graphics g)
    {
        DrawLabel(g, "INSIDE A GAME BOY", 38, Silk, new(38, 20, 720, 48), true);
        DrawLabel(g, "An exploded journey from cartridge to pixels, buttons and sound.",
            16, Muted, new(40, 76, 720, 28));
        DrawLabel(g, "THE LIVE HARDWARE ATLAS", 12, Coral, new(785, 33, 295, 25), true);
        using (var guide = new Pen(Color.FromArgb(70, 149, 170, 201), 1.5f) { DashStyle = DashStyle.Dash })
        {
            g.DrawLine(guide, 315, 383, 405, 363);
            g.DrawLine(guide, 315, 946, 405, 955);
            g.DrawLine(guide, 745, 810, 841, 680);
            g.DrawLine(guide, 745, 938, 1064, 796);
            g.DrawLine(guide, 468, 295, 468, 348);
            g.DrawLine(guide, 690, 295, 690, 348);
        }

        // Cartridge shell lifted away from its board, with exposed gold contacts.
        Round(g, new(382, 119, 350, 161), Color.FromArgb(83, 87, 107), Color.FromArgb(113, 120, 141), 16);
        Round(g, new(405, 137, 350, 160), Color.FromArgb(49, 67, 62), Coral, 14);
        DrawLabel(g, "01  /  THE CARTRIDGE", 17, Coral, new(425, 144, 300, 29), true);
        using (var pins = new SolidBrush(Color.FromArgb(239, 194, 92)))
            for (int x = 434; x < 731; x += 17) g.FillRectangle(pins, x, 286, 10, 16);
        DrawLabel(g, "A whole world, plugged in.", 16, Coral, new(43, 176, 300, 28), true);
        DrawLabel(g, "ROM supplies the instructions.", 14, Muted, new(43, 213, 320, 24));
        DrawLabel(g, "Cartridge RAM holds game data.", 14, Muted, new(43, 241, 320, 24));

        Round(g, new(39, 361, 303, 602), Color.FromArgb(47, 43, 77), Violet, 22);
        DrawLabel(g, "02  /  UNDER THE SHELL", 16, Violet, new(61, 373, 265, 29), true);
        using (var tracks = new Pen(Color.FromArgb(60, Violet), 2))
            for (int x = 55; x < 330; x += 19) g.DrawLine(tracks, x, 406, x, 946);

        // Original handheld silhouette: cream plastic, screen bezel and rounded foot.
        Round(g, new(399, 365, 382, 609), Color.FromArgb(10, 16, 31), null, 44);
        Round(g, new(385, 349, 382, 609), Color.FromArgb(224, 221, 205), Color.FromArgb(247, 244, 225), 40);
        Round(g, new(406, 386, 340, 292), Color.FromArgb(72, 79, 89), null, 22);
        DrawLabel(g, "03  /  DOT MATRIX DISPLAY", 13, Lime, new(430, 391, 300, 25), true);
        DrawLabel(g, "GAME BOY", 23, Color.FromArgb(59, 60, 101), new(413, 687, 205, 33), true);
        DrawLabel(g, "04 / PRESS TO PLAY", 12, Color.FromArgb(83, 70, 107), new(430, 821, 280, 25), true);
        using (var dpad = new SolidBrush(Color.FromArgb(41, 45, 57)))
        {
            g.FillRectangle(dpad, 451, 735, 29, 77);
            g.FillRectangle(dpad, 427, 759, 77, 29);
        }
        using (var buttons = new SolidBrush(Pink))
        {
            g.FillEllipse(buttons, 616, 770, 43, 43);
            g.FillEllipse(buttons, 678, 739, 43, 43);
        }
        DrawLabel(g, "B", 14, Silk, new(631, 778, 25, 24), true);
        DrawLabel(g, "A", 14, Silk, new(693, 747, 25, 24), true);
        using (var select = new Pen(Color.FromArgb(119, 120, 126), 9) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(select, 531, 798, 553, 789);
            g.DrawLine(select, 567, 798, 589, 789);
        }
        DrawLabel(g, "SELECT   START", 9, Color.FromArgb(83, 70, 107), new(520, 804, 105, 16));

        DrawLabel(g, "06  /  CONNECT", 17, ReadColor, new(832, 354, 245, 30), true);
        Round(g, new(837, 397, 71, 27), Color.FromArgb(88, 124, 148), ReadColor, 6);
        using (var cable = new Pen(ReadColor, 5)) g.DrawBezier(cable, 908, 410, 965, 365, 998, 439, 1062, 395);
        Round(g, new(823, 591, 262, 356), Color.FromArgb(52, 39, 55), Coral, 22);
        DrawLabel(g, "05  /  MAKE SOME NOISE", 16, Coral, new(842, 605, 230, 30), true);
        using (var speaker = new SolidBrush(Color.FromArgb(36, 35, 45))) g.FillEllipse(speaker, 874, 654, 158, 158);
        using (var ring = new Pen(Color.FromArgb(189, 133, 126), 3))
        {
            g.DrawEllipse(ring, 874, 654, 158, 158);
            g.DrawEllipse(ring, 890, 670, 126, 126);
            g.DrawEllipse(ring, 928, 708, 50, 50);
        }
        using (var sound = new Pen(Coral, 2))
        {
            g.DrawArc(sound, 1022, 681, 33, 91, -65, 130);
            g.DrawArc(sound, 1033, 668, 35, 116, -65, 130);
        }
        DrawLabel(g, "Two pulse · wave · noise", 10, Muted, new(845, 920, 227, 18));

        DrawLabel(g, "FOLLOW THE LIGHT", 15, Silk, new(40, 989, 205, 27), true);
        DotLegend(g, 258, ReadColor, "READ / FETCH");
        DotLegend(g, 440, WriteColor, "WRITE");
        DotLegend(g, 570, BothColor, "BOTH");
        DrawLabel(g, "More accesses = brighter glow", 14, Muted, new(725, 989, 350, 27));
        DrawLabel(g, "Functional exploded view • components are grouped by purpose, not literal chip placement.",
            12, Muted, new(40, 1023, 1010, 23));
    }

    private void DrawConnections(Graphics g)
    {
        foreach (ChipSpec chip in Chips)
        {
            RectangleF b = chip.Bounds;
            float y = b.Top + b.Height / 2;
            PointF[] path = chip.Node switch
            {
                HardwareNode.CartridgeRom or HardwareNode.CartridgeRam => new[] {
                    new PointF(310, 454), new PointF(364, 454), new PointF(364, 320),
                    new PointF(b.Left + b.Width / 2, 320), new PointF(b.Left + b.Width / 2, b.Bottom) },
                HardwareNode.WorkRam0 or HardwareNode.WorkRam1 or HardwareNode.HighRam or HardwareNode.Io or HardwareNode.Interrupt => new[] {
                    new PointF(75, 454), new PointF(57, 454), new PointF(57, y), new PointF(b.Left, y) },
                HardwareNode.Serial or HardwareNode.Apu or HardwareNode.Joypad => new[] {
                    new PointF(310, 454), new PointF(364, 454), new PointF(364, 330),
                    new PointF(794, 330), new PointF(794, y),
                    new PointF(chip.Node == HardwareNode.Joypad ? b.Right : b.Left, y) },
                _ => new[] { new PointF(310, 454), new PointF(365, 454), new PointF(365, y), new PointF(b.Left, y) }
            };
            Heat heat = this.heat[(int)chip.Node];
            DrawTrace(g, SignalFor(heat), heat.Strength, path);
        }
    }

    private void DrawChip(Graphics g, ChipSpec chip)
    {
        RectangleF b = chip.Bounds;
        Heat heat = this.heat[(int)chip.Node];
        Traffic traffic = this.activity[(int)chip.Node];
        Color edge = ColorBlend(chip.Accent, SignalFor(heat), heat.Strength);
        Round(g, b, Color.FromArgb(25, 34, 49), edge, 7);
        bool narrow = b.Width < 160;
        DrawLabel(g, chip.Title, narrow ? 12 : 14, edge, new(b.X + 10, b.Y + 5, b.Width - 20, 22), true);
        DrawLabel(g, chip.Address, 11, Muted, new(b.X + 10, b.Y + 27, b.Width - 20, 17));
        DrawLabel(g, chip.Node == HardwareNode.Joypad ? $"Held: {this.heldButtons}" : chip.Note,
            11, Silk, new(b.X + 10, b.Y + 47, b.Width - 20, 18));
        string counts = $"R{traffic.Reads + traffic.Fetches} W{traffic.Writes}";
        DrawLabel(g, counts, 10, Muted, narrow ? new(b.X + 10, b.Bottom - 19, b.Width - 20, 16)
            : new(b.Right - 78, b.Y + 27, 68, 17));
    }

    private void DrawCpu(Graphics g)
    {
        Round(g, new(75, 415, 235, 96), Color.FromArgb(25, 34, 49), Violet, 9);
        DrawLabel(g, "CPU  /  THE CONDUCTOR", 16, Violet, new(87, 422, 212, 26), true);
        DrawLabel(g, this.hasRom ? $"PC {this.pc:X4}  •  LR35902" : "LR35902  •  8-bit processor", 12, Silk, new(87, 452, 212, 23));
        DrawLabel(g, this.step?.Instruction.Name ?? "Step to follow an instruction", 12, ReadColor, new(87, 481, 212, 22));
    }

    private static void Round(Graphics g, RectangleF rect, Color fill, Color? edge, float radius)
    {
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(rect, new SizeF(radius, radius));
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        if (edge is Color color)
        {
            using var pen = new Pen(color, 1.5f);
            g.DrawPath(pen, path);
        }
    }

    private static void DrawLabel(Graphics g, string text, float size, Color color, RectangleF bounds, bool bold = false)
    {
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, bounds, format);
    }

    private static void DotLegend(Graphics g, float x, Color color, string label)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, x, 999, 9, 9);
        DrawLabel(g, label, 12, Silk, new(x + 18, 989, 150, 27), true);
    }
}
