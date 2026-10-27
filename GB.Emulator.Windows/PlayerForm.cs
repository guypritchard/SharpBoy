using System.IO;
using System.Diagnostics;
using GB.Emulator.Core;
using GB.Emulator.Core.Win32;
using GB.Emulator.Hosting;

namespace GB.Emulator.Windows;

internal sealed class PlayerForm : Form
{
    private readonly EmulationHost host;
    private readonly ScreenControl screen = new() { Dock = DockStyle.Fill };
    private readonly MenuStrip menu = new();
    private readonly StatusStrip status = new();
    private readonly ToolStripStatusLabel stateLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel statisticsLabel = new() { Visible = false };
    private readonly ToolStripMenuItem pauseItem = new("&Pause") { ShortcutKeys = Keys.Control | Keys.P, Enabled = false };
    private readonly ToolStripMenuItem resetItem = new("&Reset") { ShortcutKeys = Keys.Control | Keys.R, Enabled = false };
    private readonly ToolStripMenuItem muteItem = new("&Mute audio") { CheckOnClick = true };
    private readonly ToolStripMenuItem integerItem = new("&Integer scaling") { CheckOnClick = true, Checked = true };
    private readonly ToolStripMenuItem inactiveItem = new("Pause when &inactive") { CheckOnClick = true, Checked = true };
    private readonly ToolStripMenuItem fullscreenItem = new("&Fullscreen") { ShortcutKeys = Keys.F11 };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 8 };
    private readonly Stopwatch clock = new();
    private readonly PlayerKeyboardInput keyboard;
    private readonly string? initialRom;
    private InspectorForm? inspector;
    private bool loading;
    private long lastTick;
    private VideoFrame? presentedFrame;
    private Rectangle windowBounds;
    private FormWindowState previousWindowState;
    private string cartridgeDescription = "Open a ROM · Ctrl+O   |   Arrows · Z/X · Enter/Space";

    internal EmulationHost Host => this.host;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool PauseWhenInactive { get => this.inactiveItem.Checked; set => this.inactiveItem.Checked = value; }

    public PlayerForm(string? initialRom = null)
    {
        this.initialRom = initialRom;
        this.host = new EmulationHost(audio: WindowsAudioOutput.TryCreate());
        this.host.StateChanged += this.OnStateChanged;
        this.clock.Start();
        this.Text = "SharpBoy";
        this.AutoScaleDimensions = new SizeF(96, 96);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.ClientSize = new Size(640, 630);
        this.MinimumSize = new Size(336, 375);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.AllowDrop = true;
        this.BuildMenus();
        this.status.Items.AddRange([this.stateLabel, this.statisticsLabel]);
        this.Controls.Add(this.screen);
        this.Controls.Add(this.status);
        this.Controls.Add(this.menu);
        this.MainMenuStrip = this.menu;
        this.keyboard = new PlayerKeyboardInput(this, this.host.Input, () => this.host.State == EmulationState.Running);
        Application.AddMessageFilter(this.keyboard);
        this.timer.Tick += (_, _) => this.TickEmulation();
        this.DragEnter += (_, e) => e.Effect = !this.loading && GetDroppedRom(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        this.DragDrop += async (_, e) => { if (GetDroppedRom(e) is string path) await this.LoadRomAsync(path); };
        this.Deactivate += (_, _) => { if (this.host.State != EmulationState.Disposed) this.host.ReleaseAll(); };
        this.ResizeBegin += (_, _) => this.host.SetSuspended(PauseReason.Resizing, true);
        this.ResizeEnd += (_, _) => this.host.SetSuspended(PauseReason.Resizing, false);
        this.Shown += async (_, _) =>
        {
            this.timer.Start();
            if (this.initialRom != null) await this.LoadRomAsync(this.initialRom);
        };
        this.UpdateStatus();
    }

    private void BuildMenus()
    {
        var file = new ToolStripMenuItem("&File");
        var open = new ToolStripMenuItem("&Open ROM…", null, async (_, _) => await this.ChooseRomAsync())
            { ShortcutKeys = Keys.Control | Keys.O };
        var demo = new ToolStripMenuItem("Load &bundled demo", null,
            async (_, _) => await this.LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB")));
        file.DropDownItems.AddRange([open, demo, new ToolStripSeparator(), new ToolStripMenuItem("E&xit", null, (_, _) => this.Close())]);
        var emulation = new ToolStripMenuItem("&Emulation");
        this.pauseItem.Click += (_, _) => this.SetPaused(this.host.IsPlaybackRequested);
        this.resetItem.Click += (_, _) => this.ResetRom();
        this.muteItem.CheckedChanged += (_, _) => this.host.Muted = this.muteItem.Checked;
        emulation.DropDownItems.AddRange([this.pauseItem, this.resetItem, new ToolStripSeparator(), this.muteItem, this.inactiveItem]);
        var view = new ToolStripMenuItem("&View");
        var desktop = new ToolStripMenuItem("3D &desktop Game Boy…", null, (_, _) =>
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "GB.Emulator.Windows.exe")) { UseShellExecute = true };
            start.ArgumentList.Add("--desktop");
            if (this.host.Inspection.CaptureCartridge()?.Source is string source) start.ArgumentList.Add(source);
            try
            {
                using var process = Process.Start(start) ?? throw new InvalidOperationException("The desktop player did not start.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open the desktop Game Boy", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            this.Close();
        });
        this.integerItem.CheckedChanged += (_, _) => this.screen.IntegerScaling = this.integerItem.Checked;
        var scale = new ToolStripMenuItem("Window &size");
        foreach (int factor in new[] { 2, 3, 4, 5, 6 })
            scale.DropDownItems.Add(new ToolStripMenuItem($"{factor}×", null, (_, _) => this.SetScale(factor)));
        this.fullscreenItem.Click += (_, _) => this.ToggleFullscreen();
        var statisticsItem = new ToolStripMenuItem("Show &FPS and speed") { CheckOnClick = true };
        statisticsItem.CheckedChanged += (_, _) => this.statisticsLabel.Visible = statisticsItem.Checked;
        var inspect = new ToolStripMenuItem("&Inspector…", null, (_, _) => this.ShowInspector())
            { ShortcutKeys = Keys.Control | Keys.I };
        view.DropDownItems.AddRange([desktop, new ToolStripSeparator(), scale, this.integerItem, this.fullscreenItem, new ToolStripSeparator(), statisticsItem, inspect]);
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("&Controls", null, (_, _) => MessageBox.Show(this,
            "D-pad: Arrow keys\nA / B: Z / X\nStart / Select: Enter / Space\n\nOpen: Ctrl+O\nPause / resume: Ctrl+P\nReset: Ctrl+R\nInspector: Ctrl+I\nFullscreen: F11 (Esc to leave)\n\nResize the window to scale the display.\nInteger scaling preserves crisp, evenly sized pixels.",
            "SharpBoy controls", MessageBoxButtons.OK, MessageBoxIcon.Information));
        this.menu.Items.AddRange([file, emulation, view, help]);
        this.menu.MenuActivate += (_, _) => this.host.SetSuspended(PauseReason.Menu, true);
        this.menu.MenuDeactivate += (_, _) => { if (this.host.State != EmulationState.Disposed) this.host.SetSuspended(PauseReason.Menu, false); };
    }

    private async Task ChooseRomAsync()
    {
        if (this.loading) return;
        this.host.SetSuspended(PauseReason.Dialog, true);
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Open Game Boy ROM", Filter = "Game Boy ROMs (*.gb;*.gbc)|*.gb;*.gbc|All files (*.*)|*.*",
                CheckFileExists = true, Multiselect = false
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) await this.LoadRomAsync(dialog.FileName);
        }
        finally { if (!this.IsDisposed) this.host.SetSuspended(PauseReason.Dialog, false); }
    }

    internal async Task LoadRomAsync(string path)
    {
        if (this.loading || this.IsDisposed) return;
        this.loading = true;
        this.pauseItem.Enabled = this.resetItem.Enabled = false;
        try
        {
            await this.host.LoadAsync(path);
            if (this.IsDisposed) return;
            CartridgeSnapshot cart = this.host.Inspection.CaptureCartridge()!;
            string title = string.IsNullOrWhiteSpace(cart.Title) ? Path.GetFileName(path) : cart.Title;
            this.Text = $"{title} — SharpBoy";
            this.cartridgeDescription = $"{title} · {cart.Support.MapperName}" +
                (cart.Support.Level == CartridgeSupportLevel.Partial ? " · Partial support (see Inspector)" : "");
            this.PresentLatestFrame();
            this.host.Resume();
        }
        catch (Exception ex)
        {
            if (!this.IsDisposed) MessageBox.Show(this, ex.Message, "Could not load ROM", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            this.loading = false;
            if (!this.IsDisposed)
            {
                this.pauseItem.Enabled = this.resetItem.Enabled = this.host.Inspection.CaptureCartridge() != null;
                this.UpdateStatus();
            }
        }
    }

    private static string? GetDroppedRom(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
        Path.GetExtension(files[0]).ToLowerInvariant() is ".gb" or ".gbc" ? files[0] : null;

    internal void SetPaused(bool paused)
    {
        if (paused) this.host.Pause(); else this.host.Resume();
        this.UpdateStatus();
    }

    internal void ResetRom()
    {
        if (this.loading) return;
        this.host.Reset(); this.PresentLatestFrame(); this.UpdateStatus();
    }

    private void TickEmulation()
    {
        try
        {
            bool active = Form.ActiveForm == this || (this.inspector != null && Form.ActiveForm == this.inspector);
            this.host.SetSuspended(PauseReason.Inactive, this.inactiveItem.Checked && !active);
            this.host.SetSuspended(PauseReason.Minimized, this.WindowState == FormWindowState.Minimized);
            long now = this.clock.ElapsedTicks;
            TimeSpan elapsed = TimeSpan.FromSeconds((now - this.lastTick) / (double)Stopwatch.Frequency);
            this.lastTick = now;
            this.host.Advance(elapsed);
            this.PresentLatestFrame();
            EmulationStatistics statistics = this.host.Statistics;
            this.statisticsLabel.Text = this.host.State == EmulationState.Running
                ? $"{statistics.FramesPerSecond:F1} FPS · {statistics.Speed:P0} speed" : "";
            this.inspector?.RefreshState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Emulation paused", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PresentLatestFrame()
    {
        if (this.host.LatestFrame is VideoFrame frame && !ReferenceEquals(frame, this.presentedFrame))
        { this.screen.Present(frame); this.presentedFrame = frame; }
    }

    private void OnStateChanged(EmulationState state)
    {
        this.lastTick = this.clock.ElapsedTicks;
        this.UpdateStatus();
    }

    private void UpdateStatus()
    {
        this.pauseItem.Text = this.host.IsPlaybackRequested ? "&Pause" : "&Resume";
        this.stateLabel.Text = this.cartridgeDescription + (this.host.State == EmulationState.Empty ? "" : $" · {this.host.State}");
    }

    internal void SetScale(int scale)
    {
        if (scale is < 2 or > 6) throw new ArgumentOutOfRangeException(nameof(scale));
        if (this.fullscreenItem.Checked) this.ToggleFullscreen();
        this.WindowState = FormWindowState.Normal;
        this.ClientSize = new Size(Video.Width * scale, Video.Height * scale + this.menu.Height + this.status.Height);
    }

    internal void ToggleFullscreen()
    {
        if (!this.fullscreenItem.Checked)
        {
            this.previousWindowState = this.WindowState;
            this.windowBounds = this.WindowState == FormWindowState.Normal ? this.Bounds : this.RestoreBounds;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Bounds = Screen.FromControl(this).Bounds;
            this.menu.Visible = this.status.Visible = false;
            this.fullscreenItem.Checked = true;
        }
        else
        {
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Bounds = this.windowBounds;
            this.menu.Visible = this.status.Visible = true;
            this.WindowState = this.previousWindowState;
            this.fullscreenItem.Checked = false;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle these here too: the menu is hidden in fullscreen.
        if (keyData == (Keys.Control | Keys.O)) { _ = this.ChooseRomAsync(); return true; }
        if (keyData == (Keys.Control | Keys.I)) { this.ShowInspector(); return true; }
        if (keyData == (Keys.Control | Keys.P) && !this.loading && this.host.Inspection.CaptureCartridge() != null)
        { this.SetPaused(this.host.IsPlaybackRequested); return true; }
        if (keyData == (Keys.Control | Keys.R)) { this.ResetRom(); return true; }
        if (keyData == Keys.F11 || (keyData == Keys.Escape && this.fullscreenItem.Checked))
        { this.ToggleFullscreen(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal void ShowInspector()
    {
        if (this.inspector == null || this.inspector.IsDisposed)
        {
            this.inspector = new InspectorForm(this.host);
            this.inspector.FormClosed += (_, _) => this.inspector = null;
        }
        if (!this.inspector.Visible) this.inspector.Show(this);
        this.inspector.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.timer.Stop(); this.timer.Dispose();
            Application.RemoveMessageFilter(this.keyboard);
            this.host.StateChanged -= this.OnStateChanged;
            this.host.Dispose();
            this.inspector?.Dispose();
        }
        base.Dispose(disposing);
    }
}
