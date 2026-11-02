using System.Diagnostics;
using System.Text;
using GB.Emulator.Core;
using GB.Emulator.Hosting;

namespace GB.Emulator.Windows;

/// <summary>Read-only views refreshed at most four times a second, on the emulation/UI thread.</summary>
internal sealed class InspectorForm : Form
{
    private readonly EmulationHost host;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox cartridge = CreateText();
    private readonly TextBox machine = CreateText();
    private readonly TextBox memory = CreateText();
    private readonly NumericUpDown address = new() { Hexadecimal = true, Maximum = 0xFF00, Increment = 0x100, Width = 110 };
    private readonly Stopwatch refreshClock = Stopwatch.StartNew();

    public InspectorForm(EmulationHost host)
    {
        this.host = host;
        this.Text = "SharpBoy · Inspector";
        this.AutoScaleDimensions = new SizeF(96, 96);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.ClientSize = new Size(560, 490);
        this.MinimumSize = new Size(400, 330);
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.Controls.Add(this.tabs);
        var cartridgePage = new TabPage("Cartridge"); cartridgePage.Controls.Add(this.cartridge);
        var machinePage = new TabPage("CPU / hardware"); machinePage.Controls.Add(this.machine);
        var memoryPage = new TabPage("Memory");
        var memoryHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        memoryHeader.Controls.Add(new Label { Text = "Address (hex)", AutoSize = true, Padding = new Padding(0, 4, 4, 0) });
        memoryHeader.Controls.Add(this.address);
        memoryPage.Controls.Add(this.memory); memoryPage.Controls.Add(memoryHeader);
        this.tabs.TabPages.AddRange([cartridgePage, machinePage, memoryPage]);
        this.tabs.SelectedIndexChanged += (_, _) => this.RefreshState(force: true);
        this.address.ValueChanged += (_, _) => this.RefreshState(force: true);
        this.Shown += (_, _) => this.RefreshState(force: true);
        this.RefreshState(force: true);
    }

    private static TextBox CreateText() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
        ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10),
        BackColor = SystemColors.Window, Margin = new Padding(10)
    };

    public void RefreshState(bool force = false)
    {
        if (this.IsDisposed || this.host.State == EmulationState.Disposed || (!force && (!this.Visible || this.refreshClock.ElapsedMilliseconds < 250))) return;
        this.refreshClock.Restart();
        if (this.host.Inspection.CaptureCartridge() is not CartridgeSnapshot cart)
        {
            this.cartridge.Text = this.machine.Text = this.memory.Text = "Load a ROM to inspect its hardware.";
            return;
        }
        if (this.tabs.SelectedIndex == 0)
        {
            CartridgeSupport info = cart.Support;
            this.cartridge.Text = $"{cart.Title}\r\n{cart.Source}\r\n\r\n" +
                $"Type: {info.TypeName}\r\nSupport: {info.Level}\r\n" +
                $"ROM file: {cart.RomBytes:N0} bytes\r\nDeclared ROM: {cart.DeclaredRomBytes:N0} bytes\r\n" +
                $"Declared RAM: {cart.DeclaredRamBytes:N0} bytes\r\n\r\n" +
                $"ROM windows: {cart.LowerRomBank} / {cart.UpperRomBank}\r\n" +
                $"RAM bank: {cart.RamBank}\r\nRAM enabled: {cart.RamEnabled}\r\n" +
                $"Mode: {cart.BankingMode}\r\n\r\n" +
                "Available\r\n" + string.Join("\r\n", info.Available) + "\r\n\r\n" +
                "Limitations\r\n" + string.Join("\r\n", info.Missing.Concat(info.Notes));
        }
        else if (this.tabs.SelectedIndex == 1)
        {
            CpuSnapshot cpu = this.host.Inspection.CaptureCpu();
            HardwareSnapshot hardware = this.host.Inspection.CaptureHardware();
            EmulationStatistics statistics = this.host.Statistics;
            this.machine.Text = $"{statistics.FramesPerSecond:F1} FPS · {statistics.Speed:P0} speed\r\nCycles: {hardware.Cycles:N0}\r\n" +
                $"Startup: {(hardware.StartingUp ? $"{hardware.StartupFrames}/264" : "Complete")}\r\n\r\n" +
                $"PC {cpu.PC:X4}   SP {cpu.SP:X4}\r\n" +
                $"A  {cpu.A:X2}     F  {cpu.F:X2}\r\n" +
                $"BC {cpu.BC:X4}   DE {cpu.DE:X4}   HL {cpu.HL:X4}\r\n" +
                $"Z {cpu.Zero}  N {cpu.Subtract}  H {cpu.HalfCarry}  C {cpu.Carry}\r\n\r\n" +
                $"LCD scanline: {hardware.Scanline}\r\n" +
                "\r\nRegister snapshots (read-only)\r\n" +
                string.Join("\r\n", new (ushort Address, string Name)[]
                {
                    (0xFF00, "Joypad"), (0xFF04, "DIV"), (0xFF05, "TIMA"), (0xFF07, "TAC"),
                    (0xFF0F, "Interrupt flags"), (0xFFFF, "Interrupt enable"), (0xFF40, "LCDC"), (0xFF41, "STAT"),
                    (0xFF42, "Scroll Y"), (0xFF43, "Scroll X"), (0xFF44, "LY"), (0xFF47, "BG palette")
                }.Select(x => $"{x.Address:X4}  {x.Name,-18} {this.host.Inspection.ReadMemory(x.Address, 1)[0]:X2}"));
        }
        else
        {
            var text = new StringBuilder("       00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F\r\n\r\n");
            int start = (int)this.address.Value;
            MemorySnapshot snapshot = this.host.Inspection.ReadMemory((ushort)start, 256);
            for (int row = 0; row < 16; row++)
            {
                text.Append($"{start + row * 16:X4}   ");
                for (int col = 0; col < 16; col++) text.Append($"{snapshot[row * 16 + col]:X2} ");
                text.AppendLine();
            }
            this.memory.Text = text.ToString();
        }
    }
}
