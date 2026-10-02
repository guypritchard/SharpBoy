using System.Drawing;
using System.Text;
using GB.Emulator.Core;

namespace GB.Debugger;

/// <summary>Read-only cartridge header and emulator compatibility view.</summary>
internal sealed class CartridgeInfoControl : UserControl
{
    private static readonly Color Background = Color.FromArgb(20, 29, 48);
    private static readonly Color Surface = Color.FromArgb(35, 48, 69);
    private static readonly Color Foreground = Color.FromArgb(239, 244, 240);
    private static readonly Color Muted = Color.FromArgb(176, 193, 205);

    private readonly Label heading = new()
    {
        Text = "CARTRIDGE / HARDWARE PROFILE",
        Font = new Font("Segoe UI", 22, FontStyle.Bold),
        ForeColor = Foreground,
        BackColor = Background,
        AutoEllipsis = true
    };
    private readonly Label pathLabel = new()
    {
        Font = new Font("Segoe UI", 9),
        ForeColor = Muted,
        BackColor = Background,
        AutoEllipsis = true
    };
    private readonly Label statusLabel = new()
    {
        Font = new Font("Segoe UI", 12, FontStyle.Bold),
        BackColor = Surface,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 12, 0)
    };
    private readonly TextBox details = new()
    {
        Multiline = true,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        Font = new Font("Consolas", 11),
        ForeColor = Foreground,
        BackColor = Surface
    };
    private readonly Label bankLabel = new()
    {
        Font = new Font("Consolas", 10),
        ForeColor = Color.FromArgb(119, 220, 235),
        BackColor = Background,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private string staticDetails = "";
    private string currentBanks = "";
    private bool hasCartridge;

    public CartridgeInfoControl()
    {
        this.Dock = DockStyle.Fill;
        this.BackColor = Background;
        this.Controls.Add(this.details);
        this.Controls.Add(this.bankLabel);
        this.Controls.Add(this.statusLabel);
        this.Controls.Add(this.pathLabel);
        this.Controls.Add(this.heading);
        this.Resize += (_, _) => this.LayoutChildren();
        this.LayoutChildren();
        this.SetCartridge(null, null);
    }

    public void SetCartridge(Cartridge? cartridge, string? path)
    {
        this.pathLabel.Text = path ?? "";
        this.hasCartridge = cartridge != null;
        this.currentBanks = "";
        this.bankLabel.Text = "";
        if (cartridge == null)
        {
            this.statusLabel.Text = "NO CARTRIDGE LOADED";
            this.statusLabel.ForeColor = Muted;
            this.staticDetails = "Load a ROM to inspect its header and hardware requirements.";
        this.details.Text = this.staticDetails;
            return;
        }

        Header header = cartridge.Header;
        CartridgeSupport support = CartridgeSupport.Assess(cartridge);
        this.statusLabel.Text = support.Level switch
        {
            CartridgeSupportLevel.Supported => "SUPPORTED",
            CartridgeSupportLevel.Partial => "PARTIAL SUPPORT  ·  SOME FEATURES UNAVAILABLE",
            _ => "UNSUPPORTED  ·  REQUIRED HARDWARE MISSING"
        };
        this.statusLabel.ForeColor = support.Level switch
        {
            CartridgeSupportLevel.Supported => Color.FromArgb(136, 218, 157),
            CartridgeSupportLevel.Partial => Color.FromArgb(255, 205, 111),
            _ => Color.FromArgb(255, 133, 123)
        };

        var text = new StringBuilder();
        text.AppendLine(string.IsNullOrWhiteSpace(header.Title) ? "UNTITLED CARTRIDGE" : header.Title);
        text.AppendLine();
        text.AppendLine("HEADER");
        text.AppendLine($"  Type              {support.TypeName} (0x{(byte)header.CartridgeType:X2})");
        text.AppendLine($"  Mapper            {support.MapperName}");
        text.AppendLine($"  ROM               {FormatSize(header.DeclaredRomSizeBytes)} / " +
            $"{FormatBanks(header.DeclaredRomSizeBytes, 16384)}   ·   file {FormatSize(cartridge.Data.Length)}");
        text.AppendLine($"  Cartridge RAM     {FormatSize(header.DeclaredRamSizeBytes)} / " +
            FormatBanks(header.DeclaredRamSizeBytes, 8192));
        text.AppendLine($"  Display           {(header.RequiresColorGameBoy ? "CGB only" :
            header.SupportsColorGameBoy ? "DMG + CGB enhancements" : "DMG compatible")}");
        text.AppendLine($"  Super Game Boy    {(header.SupportsSuperGameBoy ? "Supported by cartridge" : "No")}");
        text.AppendLine($"  Size codes        ROM 0x{header.RomSizeCode:X2}   RAM 0x{header.RamSizeCode:X2}");
        AppendSection(text, "HARDWARE THE CARTRIDGE EXPECTS", support.Requirements);
        AppendSection(text, "AVAILABLE IN THIS EMULATOR", support.Available);
        if (support.Missing.Count > 0) AppendSection(text, "STILL NEEDED", support.Missing);
        if (support.Notes.Count > 0) AppendSection(text, "NOTES", support.Notes);
        this.staticDetails = text.ToString();
        this.UpdateDetails();
    }

    public void ShowBanks(MemoryMap memory)
    {
        if (!this.hasCartridge) return;
        string banks = "CURRENT BANK MAPPING\r\n" +
            "  0000–3FFF        ROM bank 00\r\n" +
            $"  4000–7FFF        ROM bank {memory.SelectedRomBank:X2}\r\n" +
            (memory.SelectedCartridgeRamBank is int bank
                ? $"  A000–BFFF        RAM bank {bank:X2}"
                : "  A000–BFFF        No cartridge RAM bank controller");
        if (banks == this.currentBanks) return;
        this.currentBanks = banks;
        this.bankLabel.Text = banks;
    }

    private void UpdateDetails()
    {
        this.details.Text = this.staticDetails;
    }

    private void LayoutChildren()
    {
        int width = Math.Max(0, this.ClientSize.Width - 48);
        this.heading.SetBounds(24, 16, width, 45);
        this.pathLabel.SetBounds(24, 62, width, 24);
        this.statusLabel.SetBounds(24, 95, width, 43);
        this.bankLabel.SetBounds(24, 146, width, 73);
        this.details.SetBounds(24, 226, width, Math.Max(0, this.ClientSize.Height - 250));
    }

    private static void AppendSection(StringBuilder text, string title, IReadOnlyList<string> lines)
    {
        text.AppendLine();
        text.AppendLine(title);
        foreach (string line in lines) text.AppendLine("  • " + line);
    }

    private static string FormatSize(int? bytes) => bytes switch
    {
        null => "Unknown",
        0 => "None",
        >= 1048576 => $"{bytes.Value / 1048576.0:0.##} MiB",
        _ => $"{bytes.Value / 1024} KiB"
    };

    private static string FormatBanks(int? bytes, int bankSize) => bytes switch
    {
        null => "unknown banks",
        0 => "0 banks",
        _ => $"{bytes.Value / bankSize} banks"
    };
}
