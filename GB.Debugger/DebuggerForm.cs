using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GB.Emulator.Core;
using GB.Emulator.Display;

namespace GB.Debugger;

public partial class DebuggerForm : Form
{
    private const int StackEntryCount = 8;
    private const int MemoryBytesPerRow = 16;
    private const int MemorySize = 0x10000;
    private const int MemoryRowCount = MemorySize / MemoryBytesPerRow;
    private const int CodeScrollMargin = 3;
    private const int MaxStepHistory = 200;
    private const int ScanlinesPerFrame = 154;
    private const int VBlankStartScanline = 144;
    private const int TileDataStart = 0x8000;
    private const int TileBytesPerTile = 16;
    private const int TilePixelSize = 8;
    private const int TileColumns = 16;
    private const int TileScale = 2;
    private const int TraceIntervalMs = 16;
    private const int TraceStepsPerTick = 250;
    private const string TilesLegendText = "384 tiles · VRAM 0x8000–0x97FF · raw 2bpp patterns";
    private static readonly Color[] TilePalette =
    {
        Color.White,
        Color.LightGray,
        Color.DarkGray,
        Color.Black
    };

    private readonly Gameboy gameboy = new();
    private readonly DebuggerKeyboardInput keyboardInput;
    private readonly HashSet<ushort> recentWrites = new();
    private readonly HashSet<ushort> recentReads = new();
    private readonly Dictionary<string, Label> registerLabels = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, int> disassemblyIndexByAddress = new();
    private readonly List<DebuggerSnapshot> stepHistory = new();
    private readonly System.Windows.Forms.Timer traceTimer = new();
    private readonly InteractionDiagramControl interactionDiagram = new();
    private readonly MemoryMapControl memoryMapDiagram = new();
    private readonly CartridgeInfoControl cartridgeInfo = new();
    private readonly TextBox interactionDetails = new();
    private readonly FrameDisplayControl screenDisplay = new();
    private WindowsAudioOutput? audioOutput;
    private readonly Label screenLegendLabel = new();
    private readonly Panel tilesViewport = new();
    private readonly TabPage videoTab = new("Video");
    private bool screenDirty = true;
    private bool tilesDirty = true;
    private int hoveredTile = -1;
    private CpuStepResult? lastInteractionStep;
    private byte scanlineBeforeLastStep;
    private IReadOnlyList<CpuStepResult> disassemblyCache = Array.Empty<CpuStepResult>();
    private int lastCodeIndex = -1;
    private HashSet<int> lastMemoryHighlightRows = new();
    private bool memoryViewInitialized;
    private bool traceRunning;
    private ushort stackStartPointer = Cpu.Registers.SP;
    private Cartridge? cartridge;
    private string? romPath;

    public DebuggerForm()
    {
        InitializeComponent();
        this.keyboardInput = new DebuggerKeyboardInput(this, this.gameboy.Input,
            () => this.cartridge != null, this.RefreshInputView);
        this.InitializeInteractionView();
        Application.AddMessageFilter(this.keyboardInput);
        this.Disposed += (_, _) =>
        {
            Application.RemoveMessageFilter(this.keyboardInput);
            this.audioOutput?.Dispose();
        };
        this.Deactivate += (_, _) =>
        {
            this.keyboardInput.ReleaseAll();
            this.RefreshInputView();
        };
        this.codeListBox.KeyDown += this.OnCodeListBoxKeyDown;
        this.traceTimer.Interval = TraceIntervalMs;
        this.traceTimer.Tick += this.OnTraceTick;
        this.gameboy.Video.FrameReady += (_, _) => this.screenDirty = true;
        this.CreateRegisterLabels();
        this.RefreshDebuggerViews();
        this.UpdateButtons();
    }

    private async void OnLoadRomClicked(object? sender, EventArgs e)
    {
        this.StopTrace();
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
            this.audioOutput ??= WindowsAudioOutput.TryCreate(this.gameboy.Sound);
            this.cartridge = loadedCartridge;
            this.romPath = this.openRomDialog.FileName;
            this.cartridgeInfo.SetCartridge(loadedCartridge, this.romPath);
            this.BuildDisassemblyCache();
            this.stepHistory.Clear();
            this.lastInteractionStep = null;
            this.interactionDiagram.ClearHistory();
            this.memoryMapDiagram.ClearHistory();
            this.screenDirty = this.tilesDirty = true;

            this.recentWrites.Clear();
            this.recentReads.Clear();
            this.stackStartPointer = Cpu.Registers.SP;
            this.ResetMemoryViewState();
            this.RefreshDebuggerViews();
            this.UpdateButtons(enableStep: true);

            string title = string.IsNullOrWhiteSpace(loadedCartridge.Header.Title)
                ? Path.GetFileName(this.romPath)
                : loadedCartridge.Header.Title.Trim();
            CartridgeSupport support = CartridgeSupport.Assess(loadedCartridge);
            this.statusLabel.Text = $"Loaded {title} · {support.MapperName} · {support.Level}";
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

        this.StopTrace();
        this.gameboy.Load(this.cartridge);
        this.stepHistory.Clear();
        this.lastInteractionStep = null;
        this.interactionDiagram.ClearHistory();
        this.memoryMapDiagram.ClearHistory();
        this.screenDirty = this.tilesDirty = true;
        this.recentWrites.Clear();
        this.recentReads.Clear();
        this.stackStartPointer = Cpu.Registers.SP;
        this.ResetMemoryViewState();
        this.RefreshDebuggerViews();
        this.UpdateButtons(enableStep: true);
    }

    private void OnStepClicked(object? sender, EventArgs e)
    {
        StepResult outcome = this.StepOnce(out string? errorMessage);
        switch (outcome)
        {
            case StepResult.Success:
                this.RefreshDebuggerViews(reportTiming: true);
                this.UpdateButtons(enableStep: true);
                break;
            case StepResult.NoCartridge:
                MessageBox.Show(
                    this,
                    "Load a ROM before stepping through instructions.",
                    "No ROM loaded",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;
            case StepResult.EndOfRom:
                this.RefreshDebuggerViews(reportTiming: true);
                MessageBox.Show(
                    this,
                    "Reached the end of the cartridge data.",
                    "Execution complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                this.UpdateButtons(enableStep: false);
                break;
            case StepResult.Error:
                this.RefreshDebuggerViews(reportTiming: true);
                MessageBox.Show(
                    this,
                    errorMessage ?? "Unknown execution error.",
                    "Execution error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                this.UpdateButtons(enableStep: false);
                break;
        }
    }

    private void OnTraceClicked(object? sender, EventArgs e)
    {
        if (this.cartridge == null)
        {
            MessageBox.Show(
                this,
                "Load a ROM before tracing.",
                "No ROM loaded",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (this.traceRunning)
        {
            return;
        }

        this.traceRunning = true;
        this.traceTimer.Start();
        this.UpdateButtons(enableStep: true);
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        this.StopTrace();
    }

    private void OnTraceTick(object? sender, EventArgs e)
    {
        if (!this.traceRunning)
        {
            return;
        }

        StepResult outcome = StepResult.Success;
        string? errorMessage = null;

        for (int i = 0; i < TraceStepsPerTick; i++)
        {
            outcome = this.StepOnce(out errorMessage);
            if (outcome != StepResult.Success)
            {
                break;
            }
            if (this.lastInteractionStep != null)
            {
                this.interactionDiagram.RecordTraceStep(
                    this.lastInteractionStep, this.recentReads, this.recentWrites);
                this.memoryMapDiagram.RecordTraceStep(
                    this.lastInteractionStep, this.recentReads, this.recentWrites);
            }
        }

        this.RefreshDebuggerViews();

        if (outcome == StepResult.Success)
        {
            return;
        }

        this.StopTrace();

        if (outcome == StepResult.EndOfRom)
        {
            MessageBox.Show(
                this,
                "Reached the end of the cartridge data.",
                "Execution complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            this.UpdateButtons(enableStep: false);
            return;
        }

        if (outcome == StepResult.Error)
        {
            MessageBox.Show(
                this,
                errorMessage ?? "Unknown execution error.",
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

    private void InitializeInteractionView()
    {
        Rectangle workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1380, 800);
        this.MinimumSize = new Size(Math.Min(1100, workArea.Width), Math.Min(600, workArea.Height));
        this.Size = new Size(Math.Min(1380, workArea.Width), Math.Min(800, workArea.Height));
        this.StartPosition = FormStartPosition.CenterScreen;
        this.mainSplitContainer.SplitterDistance = 465;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var memoryTab = new TabPage("Memory");
        var interactionTab = new TabPage("Interaction map");
        var memoryMapTab = new TabPage("Address map");
        var cartridgeTab = new TabPage("Cartridge");
        this.mainSplitContainer.Panel2.Controls.Remove(this.rightSplitContainer);
        this.rightSplitContainer.Dock = DockStyle.Fill;
        this.rightSplitContainer.Panel1.Controls.Remove(this.memoryGroupBox);
        memoryTab.Controls.Add(this.memoryGroupBox);

        var screenGroupBox = new GroupBox
        {
            Text = "Current screen",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        this.screenLegendLabel.Dock = DockStyle.Top;
        this.screenLegendLabel.Height = 48;
        this.screenLegendLabel.ForeColor = SystemColors.GrayText;
        this.screenLegendLabel.Text = "Current VRAM/LCD preview (not a completed PPU frame).";
        this.screenDisplay.Dock = DockStyle.Fill;
        screenGroupBox.Controls.Add(this.screenDisplay);
        screenGroupBox.Controls.Add(this.screenLegendLabel);
        this.rightSplitContainer.Panel1.Controls.Add(screenGroupBox);

        this.tilesGroupBox.Controls.Remove(this.tilesPictureBox);
        this.tilesViewport.Dock = DockStyle.Fill;
        this.tilesViewport.AutoScroll = true;
        this.tilesViewport.BackColor = Color.FromArgb(226, 232, 240);
        this.tilesPictureBox.Dock = DockStyle.None;
        this.tilesPictureBox.SizeMode = PictureBoxSizeMode.Normal;
        this.tilesPictureBox.Location = Point.Empty;
        this.tilesPictureBox.Size = new Size(
            DmgScreenRenderer.TileAtlasWidth * TileScale,
            DmgScreenRenderer.TileAtlasHeight * TileScale);
        this.tilesPictureBox.MouseMove += this.OnTileMouseMove;
        this.tilesPictureBox.MouseLeave += (_, _) =>
        {
            this.hoveredTile = -1;
            this.tilesLegendLabel.Text = TilesLegendText;
        };
        this.tilesViewport.Controls.Add(this.tilesPictureBox);
        this.tilesGroupBox.Controls.Add(this.tilesViewport);
        this.tilesGroupBox.Controls.SetChildIndex(this.tilesViewport, 0);
        this.rightSplitContainer.SplitterDistance = 390;
        this.videoTab.Controls.Add(this.rightSplitContainer);

        var interactionLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = InteractionDiagramControl.PosterBackground,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        interactionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        interactionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        interactionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        interactionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        var boardViewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = InteractionDiagramControl.PosterBackground,
            Margin = Padding.Empty
        };
        boardViewport.Controls.Add(this.interactionDiagram);
        bool fitPoster = true;
        void LayoutPoster()
        {
            if (fitPoster)
                this.interactionDiagram.SetZoom(Math.Min(
                    (boardViewport.ClientSize.Width - 8f) / InteractionDiagramControl.BoardSize.Width,
                    (boardViewport.ClientSize.Height - 8f) / InteractionDiagramControl.BoardSize.Height));
            this.interactionDiagram.Location = new Point(
                Math.Max(0, (boardViewport.ClientSize.Width - this.interactionDiagram.Width) / 2),
                Math.Max(0, (boardViewport.ClientSize.Height - this.interactionDiagram.Height) / 2));
        }
        boardViewport.Resize += (_, _) => LayoutPoster();
        var posterToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var fitButton = new Button { Text = "Fit poster", AutoSize = true };
        var actualSizeButton = new Button { Text = "100% / read labels", AutoSize = true };
        foreach (Button button in new[] { fitButton, actualSizeButton })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(38, 50, 76);
            button.ForeColor = Color.FromArgb(235, 241, 250);
            button.FlatAppearance.BorderColor = Color.FromArgb(90, 117, 152);
        }
        fitButton.Click += (_, _) => { fitPoster = true; boardViewport.AutoScrollPosition = Point.Empty; LayoutPoster(); };
        actualSizeButton.Click += (_, _) =>
        {
            fitPoster = false;
            boardViewport.AutoScrollPosition = Point.Empty;
            this.interactionDiagram.SetZoom(1);
            LayoutPoster();
        };
        posterToolbar.Controls.Add(fitButton);
        posterToolbar.Controls.Add(actualSizeButton);
        var detailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 37, 33),
            Padding = new Padding(12, 8, 12, 8),
            Margin = Padding.Empty
        };
        var detailsHeading = new Label
        {
            Dock = DockStyle.Top,
            Height = 25,
            Text = "BUS LOG  /  LAST INSTRUCTION",
            Font = new Font("Consolas", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(134, 214, 139)
        };
        this.interactionDetails.Dock = DockStyle.Fill;
        this.interactionDetails.Multiline = true;
        this.interactionDetails.ReadOnly = true;
        this.interactionDetails.WordWrap = false;
        this.interactionDetails.ScrollBars = ScrollBars.Both;
        this.interactionDetails.BackColor = Color.FromArgb(15, 37, 33);
        this.interactionDetails.ForeColor = Color.FromArgb(230, 236, 211);
        this.interactionDetails.Font = new Font("Consolas", 9F);
        this.interactionDetails.BorderStyle = BorderStyle.None;
        this.interactionDetails.Margin = Padding.Empty;
        detailsPanel.Controls.Add(this.interactionDetails);
        detailsPanel.Controls.Add(detailsHeading);
        interactionLayout.Controls.Add(posterToolbar, 0, 0);
        interactionLayout.Controls.Add(boardViewport, 0, 1);
        interactionLayout.Controls.Add(detailsPanel, 0, 2);
        interactionTab.Controls.Add(interactionLayout);

        var mapViewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(17, 26, 43)
        };
        mapViewport.Controls.Add(this.memoryMapDiagram);
        void LayoutMemoryMap()
        {
            this.memoryMapDiagram.SetZoom(Math.Min(1f,
                (mapViewport.ClientSize.Width - 8f) / MemoryMapControl.MapSize.Width));
            this.memoryMapDiagram.Location = new Point(
                Math.Max(0, (mapViewport.ClientSize.Width - this.memoryMapDiagram.Width) / 2), 0);
        }
        mapViewport.Resize += (_, _) => LayoutMemoryMap();
        memoryMapTab.Controls.Add(mapViewport);
        cartridgeTab.Controls.Add(this.cartridgeInfo);

        tabs.TabPages.Add(this.videoTab);
        tabs.TabPages.Add(interactionTab);
        tabs.TabPages.Add(memoryMapTab);
        tabs.TabPages.Add(cartridgeTab);
        tabs.TabPages.Add(memoryTab);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            this.mainSplitContainer.Panel1Collapsed = tabs.SelectedTab == interactionTab ||
                tabs.SelectedTab == memoryMapTab || tabs.SelectedTab == cartridgeTab;
            if (tabs.SelectedTab == this.videoTab) this.UpdateGraphicsView();
            if (tabs.SelectedTab == memoryMapTab) LayoutMemoryMap();
        };
        this.mainSplitContainer.Panel2.Controls.Add(tabs);
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
        this.UpdateLcdView();
        long registersTicks = Stopwatch.GetTimestamp();

        this.UpdateStackView();
        long stackTicks = Stopwatch.GetTimestamp();

        this.UpdateMemoryView();
        long memoryTicks = Stopwatch.GetTimestamp();

        this.UpdateGraphicsView();
        long tilesTicks = Stopwatch.GetTimestamp();

        this.UpdateCodeView();
        this.UpdateInteractionView();
        long codeTicks = Stopwatch.GetTimestamp();

        if (reportTiming)
        {
            this.statusLabel.Text =
                $"Step {TicksToMilliseconds(codeTicks - startTicks):0.0}ms | " +
                $"reg {TicksToMilliseconds(registersTicks - startTicks):0.0} | " +
                $"stack {TicksToMilliseconds(stackTicks - registersTicks):0.0} | " +
                $"mem {TicksToMilliseconds(memoryTicks - stackTicks):0.0} | " +
                $"tiles {TicksToMilliseconds(tilesTicks - memoryTicks):0.0} | " +
                $"code {TicksToMilliseconds(codeTicks - tilesTicks):0.0}";
        }
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private void UpdateInteractionView()
    {
        this.cartridgeInfo.ShowBanks(this.gameboy.Memory);
        this.memoryMapDiagram.ShowStep(this.lastInteractionStep, Cpu.Registers.PC,
            this.cartridge != null, this.recentReads, this.recentWrites, this.traceRunning,
            this.gameboy.Memory.SelectedRomBank, this.gameboy.Memory.SelectedCartridgeRamBank);
        this.interactionDiagram.ShowButtons(this.gameboy.Input);
        byte scanline = this.gameboy.Scanline;
        this.interactionDiagram.ShowStep(
            this.lastInteractionStep,
            Cpu.Registers.PC,
            scanline,
            this.lastInteractionStep == null ? scanline : this.scanlineBeforeLastStep,
            this.cartridge != null,
            this.recentReads,
            this.recentWrites,
            this.traceRunning);

        if (this.cartridge == null)
        {
            this.interactionDetails.Text = "Load a ROM, then press Step (F10) to follow one instruction at a time.";
            return;
        }

        if (this.lastInteractionStep == null)
        {
            this.interactionDetails.Text =
                "Press Step (F10) to see memory accesses and device register activity.\r\n" +
                "Step Back (Shift+F10) restores the previous interaction.";
            return;
        }

        var lines = new List<string>
        {
            $"Executed  {this.lastInteractionStep.Disassembly.Replace('\t', ' ')}",
            $"CPU       PC is now 0x{Cpu.Registers.PC:X4}. The opcode came from " +
                (this.lastInteractionStep.Address < 0x8000 ? "cartridge ROM." : "mapped memory."),
            FormatAccesses("READ", this.recentReads),
            FormatAccesses("WRITE", this.recentWrites),
            $"LCD       Clock advanced; scanline {this.scanlineBeforeLastStep} → {scanline}.",
            "APU       FF10–FF3F controls four sound channels and stereo routing.",
            "BOARD     Cyan = read/fetch, amber = write, violet = both. Recent activity fades over time.",
            "COUNTS    Read/write counts are unique addresses. Opcode fetch is highlighted separately."
        };
        this.interactionDetails.Lines = lines.ToArray();
    }

    private static string FormatAccesses(string label, IEnumerable<ushort> addresses)
    {
        string[] entries = addresses.OrderBy(address => address)
            .Select(address => $"0x{address:X4} ({DescribeAddress(address)})")
            .ToArray();
        return $"{label,-10}{(entries.Length == 0 ? "none" : string.Join(", ", entries))}";
    }

    private static string DescribeAddress(ushort address) => address switch
    {
        >= 0x0000 and <= 0x7FFF => "ROM",
        >= 0x8000 and <= 0x9FFF => "video RAM",
        >= 0xA000 and <= 0xBFFF => "cartridge RAM",
        >= 0xC000 and <= 0xCFFF => "work RAM 0",
        >= 0xD000 and <= 0xDFFF => "work RAM 1",
        >= 0xE000 and <= 0xFDFF => "echo range",
        >= 0xFE00 and <= 0xFE9F => "sprite OAM",
        0xFF00 => "joypad",
        0xFF01 or 0xFF02 => "serial link",
        >= 0xFF04 and <= 0xFF07 => "timer I/O",
        0xFF0F => "interrupt request",
        >= 0xFF10 and <= 0xFF3F => "APU register",
        0xFF46 => "OAM DMA",
        >= 0xFF40 and <= 0xFF4B => "LCD/PPU register",
        0xFFFF => "interrupt enable",
        >= 0xFF00 and <= 0xFF7F => "I/O",
        >= 0xFF80 and <= 0xFFFE => "high RAM",
        _ => "memory"
    };

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
        this.registerLabels["LY"].Text = $"0x{this.gameboy.Scanline:X2}";
        this.registerLabels["IF"].Text = $"0x{this.gameboy.Memory.Peek(0xFF0F):X2}";
        this.registerLabels["IE"].Text = $"0x{this.gameboy.Memory.Peek(0xFFFF):X2}";
        this.registerLabels["Flags"].Text = $"0x{Cpu.Registers.Flags:X2}";
        this.registerLabels["FlagZ"].Text = Cpu.Flags.Z ? "1" : "0";
        this.registerLabels["FlagN"].Text = Cpu.Flags.N ? "1" : "0";
        this.registerLabels["FlagH"].Text = Cpu.Flags.H ? "1" : "0";
        this.registerLabels["FlagC"].Text = Cpu.Flags.C ? "1" : "0";
    }

    private void UpdateInterruptView()
    {
        byte interruptFlags = this.gameboy.Memory.Peek(0xFF0F);
        byte interruptEnable = this.gameboy.Memory.Peek(0xFFFF);

        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x01, this.interruptIfVblankCheckBox, this.interruptIeVblankCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x02, this.interruptIfLcdStatCheckBox, this.interruptIeLcdStatCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x04, this.interruptIfTimerCheckBox, this.interruptIeTimerCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x08, this.interruptIfSerialCheckBox, this.interruptIeSerialCheckBox);
        UpdateInterruptCheckboxes(interruptFlags, interruptEnable, 0x10, this.interruptIfJoypadCheckBox, this.interruptIeJoypadCheckBox);
    }

    private void UpdateLcdView()
    {
        if (this.cartridge == null)
        {
            this.lcdScanlineValueLabel.Text = "n/a";
            this.lcdVblankValueLabel.Text = "n/a";
            this.lcdScanlineProgressBar.Enabled = false;
            this.lcdScanlineProgressBar.Value = 0;
            return;
        }

        this.lcdScanlineProgressBar.Enabled = true;
        byte scanline = this.gameboy.Scanline;
        bool vblank = scanline >= VBlankStartScanline;

        this.lcdScanlineValueLabel.Text = $"{scanline} / {ScanlinesPerFrame - 1}";
        this.lcdVblankValueLabel.Text = vblank ? "Yes" : "No";

        int value = scanline;
        if (value < this.lcdScanlineProgressBar.Minimum)
        {
            value = this.lcdScanlineProgressBar.Minimum;
        }
        if (value > this.lcdScanlineProgressBar.Maximum)
        {
            value = this.lcdScanlineProgressBar.Maximum;
        }

        this.lcdScanlineProgressBar.Value = value;
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

    private void UpdateGraphicsView()
    {
        if (this.cartridge == null)
        {
            this.tilesLegendLabel.Text = "Load a ROM to view tiles.";
            this.ReplaceTileImage(null);
            this.screenDisplay.SetFrame(null);
            this.screenLegendLabel.Text = "Load a ROM to see the screen preview.";
            return;
        }

        if (!this.videoTab.Visible) return;

        VideoState video = this.gameboy.CaptureVideoState();
        bool lcdOn = (video.LcdControl & 0x80) != 0;
        this.screenLegendLabel.Text = lcdOn
            ? $"Current screen · LCD on · SCX {video.ScrollX} · SCY {video.ScrollY}"
            : "LCD off — screen stays blank while the ROM prepares tiles. Keep tracing to see the picture.";
        this.screenLegendLabel.Text += "\nKeys: arrows move · Z=A · X=B · Enter=Start · Space=Select";
        if (!this.screenDirty && !this.tilesDirty) return;

        if (this.screenDirty)
        {
            var shades = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
            DmgScreenRenderer.RenderScreen(video, shades);
            this.screenDisplay.SetFrame(CreatePixelBitmap(shades,
                DmgScreenRenderer.Width, DmgScreenRenderer.Height, 1));
            this.screenDirty = false;
        }

        if (this.tilesDirty)
        {
            var shades = new byte[DmgScreenRenderer.TileAtlasWidth * DmgScreenRenderer.TileAtlasHeight];
            DmgScreenRenderer.RenderTileAtlas(video, shades);
            this.ReplaceTileImage(CreatePixelBitmap(shades,
                DmgScreenRenderer.TileAtlasWidth, DmgScreenRenderer.TileAtlasHeight, TileScale));
            this.tilesDirty = false;
        }

        if (this.hoveredTile < 0) this.tilesLegendLabel.Text = TilesLegendText;
    }

    private static Bitmap CreatePixelBitmap(ReadOnlySpan<byte> shades, int width, int height, int scale)
    {
        int pixelWidth = width * scale;
        int pixelHeight = height * scale;
        var bitmap = new Bitmap(pixelWidth, pixelHeight, PixelFormat.Format32bppArgb);
        var pixels = new int[pixelWidth * pixelHeight];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int color = TilePalette[shades[y * width + x]].ToArgb();
                int target = y * scale * pixelWidth + x * scale;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        pixels[target + dy * pixelWidth + dx] = color;
            }
        }

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, pixelWidth, pixelHeight),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    private void OnTileMouseMove(object? sender, MouseEventArgs e)
    {
        int column = e.X / (TilePixelSize * TileScale);
        int row = e.Y / (TilePixelSize * TileScale);
        int tile = row * TileColumns + column;
        if (column < 0 || column >= TileColumns || row < 0 || row >= DmgScreenRenderer.TileRows ||
            tile == this.hoveredTile) return;

        this.hoveredTile = tile;
        this.tilesLegendLabel.Text = $"Tile {tile} · VRAM 0x{TileDataStart + tile * TileBytesPerTile:X4}";
    }

    private void ReplaceTileImage(Image? image)
    {
        Image? previous = this.tilesPictureBox.Image;
        this.tilesPictureBox.Image = image;
        previous?.Dispose();
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
        int longestLineIndex = 0;
        int longestLineLength = 0;
        var lines = new object[disassembly.Count];

        for (int i = 0; i < disassembly.Count; i++)
        {
            CpuStepResult entry = disassembly[i];
            if (!this.disassemblyIndexByAddress.ContainsKey(entry.Address))
            {
                this.disassemblyIndexByAddress[entry.Address] = i;
            }

            string line = FormatCodeLine(entry, false);
            lines[i] = line;
            if (line.Length > longestLineLength)
            {
                longestLineIndex = i;
                longestLineLength = line.Length;
            }
        }

        this.codeListBox.Items.AddRange(lines);
        int maxWidth = TextRenderer.MeasureText(
            (string)lines[longestLineIndex], this.codeListBox.Font).Width;
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

    private void RefreshInputView()
    {
        this.interactionDiagram.ShowButtons(this.gameboy.Input);
        this.UpdateInterruptView();
        if (!this.memoryViewInitialized || this.memoryListBox.Items.Count != MemoryRowCount) return;

        const int joypadRow = 0xFF00 / MemoryBytesPerRow;
        this.memoryListBox.Items[joypadRow] = this.BuildMemoryRow(0xFF00,
            joypadRow == Cpu.Registers.PC / MemoryBytesPerRow,
            this.recentWrites.Any(write => write / MemoryBytesPerRow == joypadRow),
            this.recentReads.Any(read => read / MemoryBytesPerRow == joypadRow));
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
            this.stackStartPointer,
            this.lastInteractionStep,
            this.scanlineBeforeLastStep);
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
        if (this.traceRunning)
        {
            return;
        }

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
        this.lastInteractionStep = snapshot.LastInteractionStep;
        this.interactionDiagram.ClearHistory();
        this.memoryMapDiagram.ClearHistory();
        this.scanlineBeforeLastStep = snapshot.ScanlineBeforeLastStep;

        this.ResetMemoryViewState();
        this.screenDirty = this.tilesDirty = true;
        this.RefreshDebuggerViews();
        this.UpdateButtons(enableStep: true);
    }

    private StepResult StepOnce(out string? errorMessage)
    {
        errorMessage = null;

        if (this.cartridge == null)
        {
            return StepResult.NoCartridge;
        }

        DebuggerSnapshot snapshot = this.CaptureSnapshot();
        this.PushSnapshot(snapshot);
        byte previousScanline = this.gameboy.Scanline;

        try
        {
            CpuStepResult result = this.gameboy.Step();
            this.lastInteractionStep = result;
            this.scanlineBeforeLastStep = previousScanline;
            this.recentWrites.Clear();
            this.recentReads.Clear();
            foreach (ushort address in result.WrittenAddresses)
            {
                this.recentWrites.Add(address);
                if (address is >= 0x8000 and <= 0x9FFF or >= 0xFE00 and <= 0xFE9F or >= 0xFF40 and <= 0xFF4B)
                    this.screenDirty = true;
                if (address is >= 0x8000 and <= 0x97FF)
                    this.tilesDirty = true;
            }
            foreach (ushort address in result.ReadAddresses)
            {
                this.recentReads.Add(address);
            }

            if (result.Instruction.Value is 0x31 or 0xF9 or 0xE8)
            {
                this.stackStartPointer = Cpu.Registers.SP;
            }

            return StepResult.Success;
        }
        catch (ArgumentOutOfRangeException)
        {
            this.lastInteractionStep = null;
            this.recentWrites.Clear();
            this.recentReads.Clear();
            return StepResult.EndOfRom;
        }
        catch (Exception ex)
        {
            this.lastInteractionStep = null;
            this.recentWrites.Clear();
            this.recentReads.Clear();
            errorMessage = ex.Message;
            return StepResult.Error;
        }
    }

    private void UpdateButtons(bool enableStep = false)
    {
        bool hasCartridge = this.cartridge != null;
        this.stepButton.Enabled = hasCartridge && enableStep && !this.traceRunning;
        this.traceButton.Enabled = hasCartridge && !this.traceRunning;
        this.stopButton.Enabled = hasCartridge && this.traceRunning;
        this.resetButton.Enabled = hasCartridge && !this.traceRunning;
        this.stepBackButton.Enabled = hasCartridge && this.stepHistory.Count > 0 && !this.traceRunning;
    }

    private void StopTrace()
    {
        if (!this.traceRunning)
        {
            return;
        }

        this.traceRunning = false;
        this.traceTimer.Stop();
        this.UpdateButtons(enableStep: true);
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
            ushort stackStartPointer,
            CpuStepResult? lastInteractionStep,
            byte scanlineBeforeLastStep)
        {
            this.GameboyState = gameboyState;
            this.RecentWrites = recentWrites;
            this.RecentReads = recentReads;
            this.StackStartPointer = stackStartPointer;
            this.LastInteractionStep = lastInteractionStep;
            this.ScanlineBeforeLastStep = scanlineBeforeLastStep;
        }

        public GameboyState GameboyState { get; }

        public HashSet<ushort> RecentWrites { get; }

        public HashSet<ushort> RecentReads { get; }

        public ushort StackStartPointer { get; }

        public CpuStepResult? LastInteractionStep { get; }

        public byte ScanlineBeforeLastStep { get; }
    }

    private enum StepResult
    {
        Success,
        NoCartridge,
        EndOfRom,
        Error
    }
}
