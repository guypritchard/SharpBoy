using System.Diagnostics;
using GB.Emulator.Presentation.WinForms;
using GB.Experiments.NetworkCartridge;

namespace GB.Experiments.Windows;

internal sealed class DhcpExperimentForm : Form
{
    private readonly ScreenControl screen = new() { Dock = DockStyle.Fill, Text = "Starting network cartridge..." };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 8 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly ToolStripStatusLabel status = new("Virtual DHCP network");
    private readonly Func<IDatagramCartridgeBackend> backend;
    private DhcpSession? session;
    private long lastTick;
    private bool closing, closeReady, restarting;
    private Task cleanup = Task.CompletedTask;
    internal DhcpSession? Session => this.session;
    internal Task Cleanup => this.cleanup;

    public DhcpExperimentForm(Func<IDatagramCartridgeBackend>? backend = null, string networkName = "Virtual DHCP network")
    {
        this.backend = backend ?? (() => new DelayedDatagramBackend(new VirtualDhcpNetwork(), TimeSpan.FromMilliseconds(350)));
        this.Text = "DHCP network cartridge — SharpBoy";
        this.AutoScaleMode = AutoScaleMode.Dpi; this.ClientSize = new Size(640, 620); this.MinimumSize = new Size(336, 350);
        this.StartPosition = FormStartPosition.CenterScreen;
        var menu = new MenuStrip();
        menu.Items.Add("Restart acquisition", null, async (_, _) => await this.RestartAsync());
        var fit = new ToolStripMenuItem("Integer scaling") { CheckOnClick = true, Checked = true };
        fit.CheckedChanged += (_, _) => this.screen.IntegerScaling = fit.Checked; menu.Items.Add(fit);
        var bar = new StatusStrip(); this.status.Text = networkName; bar.Items.Add(this.status);
        this.Controls.Add(this.screen); this.Controls.Add(bar); this.Controls.Add(menu); this.MainMenuStrip = menu;
        this.timer.Tick += (_, _) => this.Tick();
        this.Shown += async (_, _) => { await this.RestartAsync(); this.timer.Start(); };
        this.FormClosing += async (_, e) =>
        {
            if (this.closeReady) return;
            e.Cancel = true;
            if (this.closing) return;
            this.closing = true; this.timer.Stop();
            try { if (this.session != null) { this.cleanup = this.session.DisposeAsync().AsTask(); await this.cleanup; } }
            finally { this.session = null; this.closeReady = true; this.Close(); }
        };
    }
    private async Task RestartAsync()
    {
        if (this.restarting || this.closing) return;
        this.restarting = true;
        DhcpSession? old = this.session; this.session = null;
        try
        {
            if (old != null) await old.DisposeAsync();
            if (this.closing) return;
            this.session = new DhcpSession(this.backend()); this.lastTick = this.clock.ElapsedTicks;
        }
        catch (Exception ex) { this.status.Text = ex.Message; }
        finally { this.restarting = false; }
    }
    private void Tick()
    {
        if (this.closing || this.restarting || this.session == null) return;
        long now = this.clock.ElapsedTicks;
        TimeSpan elapsed = TimeSpan.FromSeconds((now - this.lastTick) / (double)Stopwatch.Frequency); this.lastTick = now;
        try
        {
            this.session.Advance(elapsed);
            if (this.session.Host.LatestFrame is { } frame) this.screen.Present(frame);
        }
        catch (Exception ex) { this.status.Text = ex.Message; this.timer.Stop(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.timer.Stop(); this.timer.Dispose();
            if (this.session != null)
            {
                this.cleanup = this.session.DisposeAsync().AsTask(); this.session = null;
                _ = this.cleanup.ContinueWith(t => Debug.WriteLine(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        base.Dispose(disposing);
    }
}
