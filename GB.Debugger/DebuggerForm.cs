using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GB.Emulator.Core;

namespace GB.Debugger;

public partial class DebuggerForm : Form
{
    private const int StackEntryCount = 8;
    private const int MemoryBytesPerRow = 16;
    private const int SurroundingInstructionCount = 8;

    private readonly Gameboy gameboy = new();
    private readonly HashSet<ushort> recentWrites = new();
    private readonly Dictionary<string, Label> registerLabels = new(StringComparer.Ordinal);
    private Cartridge? cartridge;
    private string? romPath;

    public DebuggerForm()
    {
        InitializeComponent();
        this.CreateRegisterLabels();
        this.RefreshDebuggerViews();
        this.UpdateButtons();
    }

    private async void OnLoadRomClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(this.romPath))
        {
            string? directory = Path.GetDirectoryName(this.romPath);
            if (!string.IsNullOrEmpty(directory))
            {
                this.openRomDialog.InitialDirectory = directory;
            }
        }

        if (this.openRomDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var loadedCartridge = await CartridgeLoader.Load(this.openRomDialog.FileName);
            this.gameboy.Load(loadedCartridge);
            this.cartridge = loadedCartridge;
            this.romPath = this.openRomDialog.FileName;

            this.recentWrites.Clear();
            this.RefreshDebuggerViews();
            this.UpdateButtons(enableStep: true);

            string title = string.IsNullOrWhiteSpace(loadedCartridge.Header.Title)
                ? Path.GetFileName(this.romPath)
                : loadedCartridge.Header.Title.Trim();
            this.statusLabel.Text = $"Loaded {Path.GetFileName(this.romPath)} ({title})";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Failed to load ROM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        if (this.cartridge == null)
        {
            return;
        }

        this.gameboy.Load(this.cartridge);
        this.recentWrites.Clear();
        this.RefreshDebuggerViews();
        this.UpdateButtons(enableStep: true);
    }

    private void OnStepClicked(object? sender, EventArgs e)
    {
        if (this.cartridge == null)
        {
            MessageBox.Show(
                this,
                "Load a ROM before stepping through instructions.",
                "No ROM loaded",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            CpuStepResult result = this.gameboy.Step();
            this.recentWrites.Clear();
            foreach (ushort address in result.WrittenAddresses)
            {
                this.recentWrites.Add(address);
            }

            this.RefreshDebuggerViews();
        }
        catch (ArgumentOutOfRangeException)
        {
            this.recentWrites.Clear();
            this.RefreshDebuggerViews();
            MessageBox.Show(
                this,
                "Reached the end of the cartridge data.",
                "Execution complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            this.UpdateButtons(enableStep: false);
        }
        catch (Exception ex)
        {
            this.recentWrites.Clear();
            this.RefreshDebuggerViews();
            MessageBox.Show(
                this,
                ex.Message,
                "Execution error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            this.UpdateButtons(enableStep: false);
        }
    }

    private void CreateRegisterLabels()
    {
        var rows = new (string Key, string Initial)[]
        {
            ("PC", "0x0000"),
            ("SP", "0x0000"),
            ("A", "0x00"),
            ("B", "0x00"),
            ("C", "0x00"),
            ("D", "0x00"),
            ("E", "0x00"),
            ("H", "0x00"),
            ("L", "0x00"),
            ("Flags", "0x00"),
            ("FlagZ", "0"),
            ("FlagN", "0"),
            ("FlagH", "0"),
            ("FlagC", "0"),
        };

        this.registerTable.SuspendLayout();
        this.registerTable.Controls.Clear();
        this.registerTable.RowStyles.Clear();
        this.registerLabels.Clear();
        this.registerTable.RowCount = rows.Length;

        for (int row = 0; row < rows.Length; row++)
        {
            this.registerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var nameLabel = new Label
            {
                Text = rows[row].Key,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 3, 0, 3)
            };

            var valueLabel = new Label
            {
                Text = rows[row].Initial,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 3, 0, 3),
                Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point)
            };

            this.registerTable.Controls.Add(nameLabel, 0, row);
            this.registerTable.Controls.Add(valueLabel, 1, row);
            this.registerLabels[rows[row].Key] = valueLabel;
        }

        this.registerTable.ResumeLayout();
    }

    private void RefreshDebuggerViews()
    {
        this.UpdateRegisterView();

        byte[]? memorySnapshot = this.cartridge != null ? this.gameboy.Memory.Snapshot() : null;

        this.UpdateStackView(memorySnapshot);
        this.UpdateMemoryView(memorySnapshot);
        this.UpdateCodeView();
    }

    private void UpdateRegisterView()
    {
        if (this.registerLabels.Count == 0)
        {
            return;
        }

        this.registerLabels["PC"].Text = $"0x{Cpu.Registers.PC:X4}";
        this.registerLabels["SP"].Text = $"0x{Cpu.Registers.SP:X4}";
        this.registerLabels["A"].Text = $"0x{Cpu.Registers.A:X2}";
        this.registerLabels["B"].Text = $"0x{Cpu.Registers.B:X2}";
        this.registerLabels["C"].Text = $"0x{Cpu.Registers.C:X2}";
        this.registerLabels["D"].Text = $"0x{Cpu.Registers.D:X2}";
        this.registerLabels["E"].Text = $"0x{Cpu.Registers.E:X2}";
        this.registerLabels["H"].Text = $"0x{Cpu.Registers.H:X2}";
        this.registerLabels["L"].Text = $"0x{Cpu.Registers.L:X2}";
        this.registerLabels["Flags"].Text = $"0x{Cpu.Registers.Flags:X2}";
        this.registerLabels["FlagZ"].Text = Cpu.Flags.Z ? "1" : "0";
        this.registerLabels["FlagN"].Text = Cpu.Flags.N ? "1" : "0";
        this.registerLabels["FlagH"].Text = Cpu.Flags.H ? "1" : "0";
        this.registerLabels["FlagC"].Text = Cpu.Flags.C ? "1" : "0";
    }

    private void UpdateStackView(byte[]? memorySnapshot)
    {
        this.stackListBox.BeginUpdate();
        this.stackListBox.Items.Clear();

        if (memorySnapshot == null)
        {
            this.stackListBox.Items.Add("Load a ROM to view stack.");
            this.stackListBox.EndUpdate();
            return;
        }

        ushort sp = Cpu.Registers.SP;
        for (int index = 0; index < StackEntryCount; index++)
        {
            int address = sp + (index * 2);
            if (address >= memorySnapshot.Length)
            {
                break;
            }

            byte low = memorySnapshot[address];
            byte high = address + 1 < memorySnapshot.Length ? memorySnapshot[address + 1] : (byte)0x00;
            ushort value = (ushort)(low | (high << 8));
            string prefix = index == 0 ? "->" : "  ";
            this.stackListBox.Items.Add($"{prefix} 0x{address:X4}: 0x{value:X4}");
        }

        if (this.stackListBox.Items.Count == 0)
        {
            this.stackListBox.Items.Add("Stack is empty.");
        }

        this.stackListBox.EndUpdate();
    }

    private void UpdateMemoryView(byte[]? memorySnapshot)
    {
        this.memoryListBox.BeginUpdate();
        this.memoryListBox.Items.Clear();

        if (memorySnapshot == null)
        {
            this.memoryListBox.Items.Add("Load a ROM to view memory.");
            this.memoryListBox.EndUpdate();
            return;
        }

        for (int address = 0; address < memorySnapshot.Length; address += MemoryBytesPerRow)
        {
            bool isPcRow = Cpu.Registers.PC >= address && Cpu.Registers.PC < address + MemoryBytesPerRow;
            bool hasRecentWrite = this.recentWrites.Any(write => write >= address && write < address + MemoryBytesPerRow);

            var builder = new StringBuilder(14 + MemoryBytesPerRow * 3);
            builder.Append(isPcRow ? '>' : ' ');
            builder.Append(hasRecentWrite ? '*' : ' ');
            builder.Append(' ');
            builder.Append($"0x{address:X4}:");

            for (int offset = 0; offset < MemoryBytesPerRow && address + offset < memorySnapshot.Length; offset++)
            {
                builder.Append(' ');
                builder.Append(memorySnapshot[address + offset].ToString("X2"));
            }

            this.memoryListBox.Items.Add(builder.ToString());
        }

        this.memoryListBox.EndUpdate();
    }

    private void UpdateCodeView()
    {
        this.codeListBox.BeginUpdate();
        this.codeListBox.Items.Clear();

        if (this.cartridge == null)
        {
            this.codeListBox.Items.Add("Load a ROM to view code.");
            this.codeListBox.EndUpdate();
            return;
        }

        IReadOnlyList<CpuStepResult> window;
        try
        {
            window = this.gameboy.GetInstructionWindow(SurroundingInstructionCount, SurroundingInstructionCount);
        }
        catch (Exception ex)
        {
            this.codeListBox.Items.Add($"Unable to disassemble: {ex.Message}");
            this.codeListBox.EndUpdate();
            return;
        }

        if (window.Count == 0)
        {
            this.codeListBox.Items.Add("Unable to disassemble at current PC.");
            this.codeListBox.EndUpdate();
            return;
        }

        int selectedIndex = -1;
        for (int i = 0; i < window.Count; i++)
        {
            bool isCurrent = window[i].Address == Cpu.Registers.PC;
            if (isCurrent)
            {
                selectedIndex = i;
            }

            string prefix = isCurrent ? ">" : " ";
            this.codeListBox.Items.Add($"{prefix} {window[i].Disassembly}");
        }

        if (selectedIndex >= 0)
        {
            this.codeListBox.SelectedIndex = selectedIndex;
            this.codeListBox.TopIndex = Math.Max(0, selectedIndex - 3);
        }

        this.codeListBox.EndUpdate();
    }

    private void UpdateButtons(bool enableStep = false)
    {
        bool hasCartridge = this.cartridge != null;
        this.stepButton.Enabled = hasCartridge && enableStep;
        this.resetButton.Enabled = hasCartridge;
    }
}
