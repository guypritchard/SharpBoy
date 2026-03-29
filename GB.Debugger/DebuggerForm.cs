using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GB.Emulator.Core;

namespace GB.Debugger;

public partial class DebuggerForm : Form
{
    private const int StackEntryCount = 8;
    private const int MemoryBytesPerRow = 16;
    private const int MemorySize = 0x10000;
    private const int MemoryRowCount = MemorySize / MemoryBytesPerRow;
    private const int CodeScrollMargin = 3;
    private const int MaxStepHistory = 200;

    private readonly Gameboy gameboy = new();
    private readonly HashSet<ushort> recentWrites = new();
    private readonly HashSet<ushort> recentReads = new();
    private readonly Dictionary<string, Label> registerLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, int> disassemblyIndexByAddress = new();
    private readonly List<DebuggerSnapshot> stepHistory = new();
    private IReadOnlyList<CpuStepResult> disassemblyCache = Array.Empty<CpuStepResult>();
    private int lastCodeIndex = -1;
    private HashSet<int> lastMemoryHighlightRows = new();
    private bool memoryViewInitialized;
    private ushort stackStartPointer = Cpu.Registers.SP;
    private Cartridge? cartridge;
    private string? romPath;

    public DebuggerForm()
    {
        InitializeComponent();
        this.codeListBox.KeyDown += this.OnCodeListBoxKeyDown;
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
            this.BuildDisassemblyCache();
            this.stepHistory.Clear();

            this.recentWrites.Clear();
            this.recentReads.Clear();
            this.stackStartPointer = Cpu.Registers.SP;
            this.ResetMemoryViewState();
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
        this.stepHistory.Clear();
        this.recentWrites.Clear();
        this.recentReads.Clear();
        this.stackStartPointer = Cpu.Registers.SP;
        this.ResetMemoryViewState();
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

        DebuggerSnapshot snapshot = this.CaptureSnapshot();
        this.PushSnapshot(snapshot);

        try
        {
            CpuStepResult result = this.gameboy.Step();
            this.recentWrites.Clear();
            this.recentReads.Clear();
            foreach (ushort address in result.WrittenAddresses)
            {
                this.recentWrites.Add(address);
            }
            foreach (ushort address in result.ReadAddresses)
            {
                this.recentReads.Add(address);
            }

            if (result.Instruction.Value is 0x31 or 0xF9 or 0xE8)
            {
                this.stackStartPointer = Cpu.Registers.SP;
            }

            this.RefreshDebuggerViews(reportTiming: true);
            this.UpdateButtons(enableStep: true);
        }
        catch (ArgumentOutOfRangeException)
        {
            this.recentWrites.Clear();
            this.recentReads.Clear();
            this.RefreshDebuggerViews(reportTiming: true);
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
            this.recentReads.Clear();
            this.RefreshDebuggerViews(reportTiming: true);
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
            ("LY", "0x00"),
            ("IF", "0x00"),
            ("IE", "0x00"),
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
        this.RefreshDebuggerViews(reportTiming: false);
    }

    private void RefreshDebuggerViews(bool reportTiming)
    {
        long startTicks = Stopwatch.GetTimestamp();
        this.UpdateRegisterView();
        this.UpdateInterruptView();
        long registersTicks = Stopwatch.GetTimestamp();

        this.UpdateStackView();
        long stackTicks = Stopwatch.GetTimestamp();

        this.UpdateMemoryView();
        long memoryTicks = Stopwatch.GetTimestamp();

        this.UpdateCodeView();
        long codeTicks = Stopwatch.GetTimestamp();

        if (reportTiming)
        {
            this.statusLabel.Text =
                $"Step {TicksToMilliseconds(codeTicks - startTicks):0.0}ms | " +
                $"reg {TicksToMilliseconds(registersTicks - startTicks):0.0} | " +
                $"stack {TicksToMilliseconds(stackTicks - registersTicks):0.0} | " +
                $"mem {TicksToMilliseconds(memoryTicks - stackTicks):0.0} | " +
                $"code {TicksToMilliseconds(codeTicks - memoryTicks):0.0}";
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
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
        this.registerLabels["LY"].Text = $"0x{this.gameboy.Memory.Read8(0xFF44):X2}";
        this.registerLabels["IF"].Text = $"0x{this.gameboy.Memory.Read8(0xFF0F):X2}";
        this.registerLabels["IE"].Text = $"0x{this.gameboy.Memory.Read8(0xFFFF):X2}";
        this.registerLabels["Flags"].Text = $"0x{Cpu.Registers.Flags:X2}";
        this.registerLabels["FlagZ"].Text = Cpu.Flags.Z ? "1" : "0";
        this.registerLabels["FlagN"].Text = Cpu.Flags.N ? "1" : "0";
        this.registerLabels["FlagH"].Text = Cpu.Flags.H ? "1" : "0";
        this.registerLabels["FlagC"].Text = Cpu.Flags.C ? "1" : "0";
    }

    private void UpdateInterruptView()
    {
        byte interruptFlags = this.gameboy.Memory.Read8(0xFF0F);
        byte interruptEnable = this.gameboy.Memory.Read8(0xFFFF);

        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x01, this.interruptIfVblankCheckBox, this.interruptIeVblankCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x02, this.interruptIfLcdStatCheckBox, this.interruptIeLcdStatCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x04, this.interruptIfTimerCheckBox, this.interruptIeTimerCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x08, this.interruptIfSerialCheckBox, this.interruptIeSerialCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x10, this.interruptIfJoypadCheckBox, this.interruptIeJoypadCheckBox);
    }

    private static void UpdateInterruptCheckboxes(byte flags, byte enable, byte mask, CheckBox flagsCheckBox, CheckBox enableCheckBox)
    {
        flagsCheckBox.Checked = (flags & mask) != 0;
        enableCheckBox.Checked = (enable & mask) != 0;
    }

    private void UpdateStackView()
    {
        this.stackListBox.BeginUpdate();
        this.stackListBox.Items.Clear();

        if (this.cartridge == null)
        {
            this.stackListBox.Items.Add("Load a ROM to view stack.");
            this.stackListBox.EndUpdate();
            return;
        }

        ushort sp = Cpu.Registers.SP;
        ushort upperBound = this.stackStartPointer >= sp ? this.stackStartPointer : sp;
        int wordCount = (upperBound - sp) / 2 + 1;

        for (int index = 0; index < wordCount; index++)
        {
            int address = sp + (index * 2);
            if (address >= MemorySize)
            {
                break;
            }

            byte low = this.gameboy.Memory.Peek((ushort)address);
            byte high = address + 1 < MemorySize ? this.gameboy.Memory.Peek((ushort)(address + 1)) : (byte)0x00;
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

    private void UpdateMemoryView()
    {
        this.memoryListBox.BeginUpdate();

        if (this.cartridge == null)
        {
            this.memoryListBox.Items.Clear();
            this.memoryListBox.Items.Add("Load a ROM to view memory.");
            this.memoryViewInitialized = false;
            this.lastMemoryHighlightRows.Clear();
            this.memoryListBox.EndUpdate();
            return;
        }

        int currentPcRow = Cpu.Registers.PC / MemoryBytesPerRow;
        HashSet<int> rowsWithWrite = this.BuildRowsForAddresses(this.recentWrites);
        HashSet<int> rowsWithRead = this.BuildRowsForAddresses(this.recentReads);

        if (!this.memoryViewInitialized || this.memoryListBox.Items.Count != MemoryRowCount)
        {
            this.memoryListBox.Items.Clear();
            for (int row = 0; row < MemoryRowCount; row++)
            {
                int address = row * MemoryBytesPerRow;
                this.memoryListBox.Items.Add(
                    this.BuildMemoryRow(
                        address,
                        row == currentPcRow,
                        rowsWithWrite.Contains(row),
                        rowsWithRead.Contains(row)));
            }

            this.memoryViewInitialized = true;
            this.lastMemoryHighlightRows = new HashSet<int>(rowsWithWrite);
            this.lastMemoryHighlightRows.UnionWith(rowsWithRead);
            this.lastMemoryHighlightRows.Add(currentPcRow);
            this.memoryListBox.EndUpdate();
            return;
        }

        var newHighlightRows = new HashSet<int>(rowsWithWrite);
        newHighlightRows.UnionWith(rowsWithRead);
        newHighlightRows.Add(currentPcRow);

        var rowsToUpdate = new HashSet<int>(this.lastMemoryHighlightRows);
        rowsToUpdate.UnionWith(newHighlightRows);

        foreach (int row in rowsToUpdate)
        {
            if (row < 0 || row >= MemoryRowCount)
            {
                continue;
            }

            int address = row * MemoryBytesPerRow;
            this.memoryListBox.Items[row] = this.BuildMemoryRow(
                address,
                row == currentPcRow,
                rowsWithWrite.Contains(row),
                rowsWithRead.Contains(row));
        }

        this.lastMemoryHighlightRows = newHighlightRows;
        this.memoryListBox.EndUpdate();
    }

    private HashSet<int> BuildRowsForAddresses(IEnumerable<ushort> addresses)
    {
        var rows = new HashSet<int>();
        foreach (ushort address in addresses)
        {
            rows.Add(address / MemoryBytesPerRow);
        }

        return rows;
    }

    private string BuildMemoryRow(int address, bool isPcRow, bool hasRecentWrite, bool hasRecentRead)
    {
        var builder = new StringBuilder(15 + MemoryBytesPerRow * 3);
        builder.Append(isPcRow ? '>' : ' ');
        builder.Append(hasRecentWrite ? '*' : ' ');
        builder.Append(hasRecentRead ? 'r' : ' ');
        builder.Append(' ');
        builder.Append($"0x{address:X4}:");

        for (int offset = 0; offset < MemoryBytesPerRow && address + offset < MemorySize; offset++)
        {
            builder.Append(' ');
            byte value = this.gameboy.Memory.Peek((ushort)(address + offset));
            builder.Append(value.ToString("X2"));
        }

        return builder.ToString();
    }

    private void ResetMemoryViewState()
    {
        this.memoryViewInitialized = false;
        this.lastMemoryHighlightRows.Clear();
    }

    private void BuildDisassemblyCache()
    {
        this.disassemblyCache = Array.Empty<CpuStepResult>();
        this.disassemblyIndexByAddress.Clear();
        this.lastCodeIndex = -1;

        this.codeListBox.BeginUpdate();
        this.codeListBox.Items.Clear();

        if (this.cartridge == null)
        {
            this.codeListBox.Items.Add("Load a ROM to view code.");
            this.codeListBox.EndUpdate();
            return;
        }

        IReadOnlyList<CpuStepResult> disassembly;
        try
        {
            disassembly = this.gameboy.GetDisassembly();
        }
        catch (Exception ex)
        {
            this.codeListBox.Items.Add($"Unable to disassemble: {ex.Message}");
            this.codeListBox.EndUpdate();
            return;
        }

        if (disassembly.Count == 0)
        {
            this.codeListBox.Items.Add("Unable to disassemble ROM.");
            this.codeListBox.EndUpdate();
            return;
        }

        this.disassemblyCache = disassembly;
        int maxWidth = 0;

        for (int i = 0; i < disassembly.Count; i++)
        {
            CpuStepResult entry = disassembly[i];
            if (!this.disassemblyIndexByAddress.ContainsKey(entry.Address))
            {
                this.disassemblyIndexByAddress[entry.Address] = i;
            }

            string line = FormatCodeLine(entry, false);
            this.codeListBox.Items.Add(line);

            int width = TextRenderer.MeasureText(line, this.codeListBox.Font).Width;
            if (width > maxWidth)
            {
                maxWidth = width;
            }
        }

        this.codeListBox.HorizontalExtent = Math.Max(maxWidth + 8, this.codeListBox.ClientSize.Width);
        this.codeListBox.EndUpdate();
    }

    private static string FormatCodeLine(CpuStepResult entry, bool isCurrent)
    {
        string prefix = isCurrent ? ">" : " ";
        return $"{prefix} {entry.Disassembly}";
    }

    private void UpdateCodeView()
    {
        if (this.cartridge == null)
        {
            this.BuildDisassemblyCache();
            return;
        }

        if (this.disassemblyCache.Count == 0)
        {
            this.BuildDisassemblyCache();
            if (this.disassemblyCache.Count == 0)
            {
                return;
            }
        }

        if (!this.disassemblyIndexByAddress.TryGetValue(Cpu.Registers.PC, out int currentIndex))
        {
            if (this.lastCodeIndex >= 0 && this.lastCodeIndex < this.codeListBox.Items.Count)
            {
                CpuStepResult previous = this.disassemblyCache[this.lastCodeIndex];
                this.codeListBox.Items[this.lastCodeIndex] = FormatCodeLine(previous, false);
                this.lastCodeIndex = -1;
            }

            return;
        }

        this.codeListBox.BeginUpdate();

        if (this.lastCodeIndex >= 0 && this.lastCodeIndex != currentIndex)
        {
            CpuStepResult previous = this.disassemblyCache[this.lastCodeIndex];
            this.codeListBox.Items[this.lastCodeIndex] = FormatCodeLine(previous, false);
        }

        CpuStepResult current = this.disassemblyCache[currentIndex];
        this.codeListBox.Items[currentIndex] = FormatCodeLine(current, true);
        this.lastCodeIndex = currentIndex;

        this.codeListBox.EndUpdate();

        this.EnsureCodeRowVisible(currentIndex);
    }

    private void EnsureCodeRowVisible(int index)
    {
        if (index < 0 || index >= this.codeListBox.Items.Count)
        {
            return;
        }

        int visibleCount = Math.Max(1, this.codeListBox.ClientSize.Height / this.codeListBox.ItemHeight);
        int topIndex = this.codeListBox.TopIndex;
        int bottomIndex = topIndex + visibleCount - 1;

        if (index < topIndex || index > bottomIndex)
        {
            int newTopIndex = Math.Max(0, index - CodeScrollMargin);
            int maxTopIndex = Math.Max(0, this.codeListBox.Items.Count - visibleCount);
            this.codeListBox.TopIndex = Math.Min(newTopIndex, maxTopIndex);
        }
    }

    private void OnCodeListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C)
        {
            this.CopySelectedCodeLines();
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.A)
        {
            this.SelectAllCodeLines();
            e.Handled = true;
        }
    }

    private void CopySelectedCodeLines()
    {
        if (this.codeListBox.Items.Count == 0)
        {
            return;
        }

        var lines = new List<string>();
        foreach (object item in this.codeListBox.SelectedItems)
        {
            string? line = item?.ToString();
            if (!string.IsNullOrEmpty(line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0 && this.lastCodeIndex >= 0 && this.lastCodeIndex < this.codeListBox.Items.Count)
        {
            string? line = this.codeListBox.Items[this.lastCodeIndex]?.ToString();
            if (!string.IsNullOrEmpty(line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }

    private void SelectAllCodeLines()
    {
        if (this.codeListBox.Items.Count == 0)
        {
            return;
        }

        this.codeListBox.BeginUpdate();
        for (int i = 0; i < this.codeListBox.Items.Count; i++)
        {
            this.codeListBox.SetSelected(i, true);
        }
        this.codeListBox.EndUpdate();
    }

    private DebuggerSnapshot CaptureSnapshot()
    {
        return new DebuggerSnapshot(
            this.gameboy.CaptureState(),
            new HashSet<ushort>(this.recentWrites),
            new HashSet<ushort>(this.recentReads),
            this.stackStartPointer);
    }

    private void PushSnapshot(DebuggerSnapshot snapshot)
    {
        this.stepHistory.Add(snapshot);
        if (this.stepHistory.Count > MaxStepHistory)
        {
            this.stepHistory.RemoveAt(0);
        }
    }

    private void OnStepBackClicked(object? sender, EventArgs e)
    {
        if (this.stepHistory.Count == 0)
        {
            return;
        }

        DebuggerSnapshot snapshot = this.stepHistory[^1];
        this.stepHistory.RemoveAt(this.stepHistory.Count - 1);

        this.gameboy.RestoreState(snapshot.GameboyState);
        this.recentWrites.Clear();
        this.recentReads.Clear();
        this.recentWrites.UnionWith(snapshot.RecentWrites);
        this.recentReads.UnionWith(snapshot.RecentReads);
        this.stackStartPointer = snapshot.StackStartPointer;

        this.ResetMemoryViewState();
        this.RefreshDebuggerViews();
        this.UpdateButtons(enableStep: true);
    }

    private void UpdateButtons(bool enableStep = false)
    {
        bool hasCartridge = this.cartridge != null;
        this.stepButton.Enabled = hasCartridge && enableStep;
        this.resetButton.Enabled = hasCartridge;
        this.stepBackButton.Enabled = hasCartridge && this.stepHistory.Count > 0;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Shift | Keys.F10))
        {
            if (this.stepBackButton.Enabled)
            {
                this.OnStepBackClicked(this.stepBackButton, EventArgs.Empty);
            }

            return true;
        }

        if (keyData == Keys.F10)
        {
            if (this.stepButton.Enabled)
            {
                this.OnStepClicked(this.stepButton, EventArgs.Empty);
            }

            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private sealed class DebuggerSnapshot
    {
        public DebuggerSnapshot(
            GameboyState gameboyState,
            HashSet<ushort> recentWrites,
            HashSet<ushort> recentReads,
            ushort stackStartPointer)
        {
            this.GameboyState = gameboyState;
            this.RecentWrites = recentWrites;
            this.RecentReads = recentReads;
            this.StackStartPointer = stackStartPointer;
        }

        public GameboyState GameboyState { get; }

        public HashSet<ushort> RecentWrites { get; }

        public HashSet<ushort> RecentReads { get; }

        public ushort StackStartPointer { get; }
    }
}
