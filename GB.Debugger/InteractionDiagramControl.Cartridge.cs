using System.Drawing.Drawing2D;

namespace GB.Debugger;

internal sealed partial class InteractionDiagramControl
{
    private bool HasCartridgeRam => this.cartridgeSupport?.Requirements.Contains("Cartridge RAM") == true;
    private Color MapperAccent => this.cartridgeSupport?.MapperName switch
    {
        "MBC1" => Violet,
        "MBC3" => ReadColor,
        "None" => Coral,
        _ => WriteColor
    };

    private void DrawMapper(Graphics g, RectangleF bounds)
    {
        Heat heat = this.heat[(int)HardwareNode.CartridgeMapper];
        Traffic traffic = this.activity[(int)HardwareNode.CartridgeMapper];
        Color accent = this.diagramCartridge == null ? Copper : this.MapperAccent;
        Color edge = ColorBlend(accent, SignalFor(heat), heat.Strength);
        Round(g, bounds, Color.FromArgb(25, 34, 49), edge, 7);
        bool unavailable = this.HasBankController && !this.HasActiveMapper;
        if (unavailable)
        {
            using var outline = new Pen(WriteColor, 2) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(outline, bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6);
        }
        string title = this.diagramCartridge == null ? "CARTRIDGE HARDWARE"
            : this.HasBankController ? $"{this.cartridgeSupport!.MapperName}  /  BANK CONTROLLER"
            : "FIXED CONNECTION  /  NO MAPPER";
        DrawLabel(g, title, 12, edge, new(bounds.X + 10, bounds.Y + 3, bounds.Width - 20, 23), true);
        string mode = this.diagramCartridge == null ? "Load a ROM to reveal its mapper"
            : unavailable ? "Required hardware · not emulated"
            : this.HasActiveMapper ? this.bankingMode : "ROM wired directly to the address bus";
        DrawLabel(g, mode, 11, Silk, new(bounds.X + 10, bounds.Y + 29, bounds.Width - 68, 20));
        if (this.HasBankController)
            DrawLabel(g, $"W{traffic.Writes}", 11, heat.Write > 0 ? WriteColor : Muted,
                new(bounds.Right - 48, bounds.Y + 29, 38, 20), true);
    }

    private void DrawCartridgeProfile(Graphics g)
    {
        Round(g, new(790, 130, 295, 202), Color.FromArgb(25, 34, 49), this.MapperAccent, 12);
        DrawLabel(g, "CARTRIDGE CAPABILITIES", 12, Muted, new(804, 140, 265, 20), true);
        if (this.cartridgeSupport == null)
        {
            DrawLabel(g, "Waiting for a cartridge", 17, Silk, new(804, 175, 265, 28), true);
            DrawLabel(g, "Mapper, banks and optional hardware", 12, Muted, new(804, 215, 265, 22));
            DrawLabel(g, "appear here when a ROM is loaded.", 12, Muted, new(804, 238, 265, 22));
            return;
        }

        string name = this.HasBankController ? this.cartridgeSupport.MapperName : "ROM ONLY";
        DrawLabel(g, name, 23, this.MapperAccent, new(804, 162, 135, 33), true);
        string status = this.cartridgeSupport.Level switch
        {
            GB.Emulator.Core.CartridgeSupportLevel.Supported => "SUPPORTED",
            GB.Emulator.Core.CartridgeSupportLevel.Partial => "PARTIAL",
            _ => "UNSUPPORTED"
        };
        Color statusColor = this.cartridgeSupport.Level == GB.Emulator.Core.CartridgeSupportLevel.Supported
            ? Lime : WriteColor;
        DrawLabel(g, status, 11, statusColor, new(931, 167, 140, 25), true);
        string rom = this.HasBankController && !this.HasActiveMapper ? "ROM banking unavailable"
            : $"ROM  0000: {this.lowerRomBank:X2}   4000: {this.upperRomBank:X2}";
        DrawLabel(g, rom, 13, Silk, new(804, 202, 267, 22));
        string ram = !this.HasCartridgeRam ? "RAM  Not fitted"
            : this.HasActiveMapper ? $"RAM  Bank {this.ramBank:X2} · {(this.ramEnabled ? "enabled" : "disabled")}" : "RAM  Required · unavailable";
        DrawLabel(g, ram, 13, this.ramEnabled ? Lime : Muted, new(804, 228, 267, 22));
        var features = new List<string>();
        if (this.cartridgeSupport.Requirements.Contains("Battery-backed saves"))
            features.Add(this.HasActiveMapper && this.HasCartridgeRam ? "Battery saves: session only" : "Battery saves: unavailable");
        if (this.cartridgeSupport.Requirements.Contains("Real-time clock")) features.Add("RTC: unavailable");
        if (this.cartridgeSupport.Requirements.Contains("Rumble motor")) features.Add("Rumble: unavailable");
        if (this.cartridgeSupport.Requirements.Contains("Motion sensor")) features.Add("Sensor: unavailable");
        if (features.Count == 0) features.Add(this.HasActiveMapper ? this.bankingMode : "No optional cartridge hardware");
        for (int i = 0; i < Math.Min(2, features.Count); i++)
            DrawLabel(g, features[i], 12, features[i].Contains("unavailable") ? WriteColor : Muted,
                new(804, 264 + i * 24, 267, 22));
    }
}
