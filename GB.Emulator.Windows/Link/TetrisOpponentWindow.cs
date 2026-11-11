using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GB.Emulator.AI.Tetris;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using GB.Emulator.Link;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace GB.Emulator.Windows.Link;

/// <summary>Owns a second ordinary emulator; only the observer is specific to Tetris.</summary>
internal sealed class TetrisOpponentWindow : Window
{
    private readonly EmulationHost host = new(options: new() { HistoryCapacity = 0 });
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CheckBox automatic = new() { Content = "Enable AI", Foreground = Brushes.White, IsEnabled = false, Margin = new Thickness(0, 10, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap }, decision = new() { TextWrapping = TextWrapping.Wrap };
    private readonly WriteableBitmap lcd = new(160, 144, 96, 96, PixelFormats.Indexed8,
        new BitmapPalette(new[] { Color.FromRgb(195, 207, 148), Color.FromRgb(143, 161, 92), Color.FromRgb(76, 103, 57), Color.FromRgb(29, 50, 31) }));
    private readonly Canvas board = new() { Width = 120, Height = 240, Background = Brushes.Black };
    private LinkLobbyClient? client;
    private TetrisController? controller;
    private TetrisAiSettings style = new();
    private TetrisStyleWindow? styleWindow;
    private Guid attached;
    private readonly Guid invitedBy;
    private Guid acceptedInvitation;
    private readonly byte[] rom;
    private TimeSpan previous;
    private bool closed;
    private int wakeQueued;
    private VideoFrame? presented;
    private TetrisObservation? drawnObservation;
    private TetrisPlan? drawnPlan;
    internal Task Shutdown { get; private set; } = Task.CompletedTask;
    internal EmulationHost Host => host;
    internal LinkLobbyClient? Client => client;
    internal void ConfigureStyle(TetrisAiSettings settings)
    {
        settings.Validate(); style = settings; controller?.Configure(settings);
    }

    internal TetrisOpponentWindow(byte[] rom, Guid invitedBy)
    {
        this.rom = rom; this.invitedBy = invitedBy;
        Title = "SharpBoy · Tetris opponent"; Width = 880; Height = 700; MinWidth = 740; MinHeight = 540;
        Background = new SolidColorBrush(Color.FromRgb(23, 27, 25)); Foreground = Brushes.White;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
        var root = new DockPanel { Margin = new Thickness(20), Background = Background };
        var controls = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
        var controlsScroll = new ScrollViewer { Content = controls, Width = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(controlsScroll, Dock.Right); root.Children.Add(controlsScroll);
        controls.Children.Add(new TextBlock { Text = "PLAYER TWO / TETRIS AI", FontWeight = FontWeights.Bold, FontSize = 18 });
        controls.Children.Add(status);
        var profile = new Button { Content = "Load ROM reader profile…", Margin = new Thickness(0, 12, 0, 0) };
        profile.Click += (_, _) => LoadProfile(); controls.Children.Add(profile); controls.Children.Add(automatic);
        var playingStyle = new Button { Content = "Playing style / sliders…", Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 10) };
        playingStyle.Click += (_, _) => OpenStyle(); controls.Children.Add(playingStyle);
        automatic.Unchecked += (_, _) => { if (!closed) controller?.Stop(host); };
        automatic.Checked += (_, _) => { if (!closed) host.ReleaseAll(); };
        controls.Children.Add(new TextBlock { Text = "AI off: use arrows, Z/A, X/B, Enter/Start and Space/Select for menus.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var key in new[] { GameBoyButton.Start, GameBoyButton.Select, GameBoyButton.A, GameBoyButton.B })
        {
            var button = new Button { Content = key.ToString(), Margin = new Thickness(0, 8, 8, 8), Padding = new Thickness(8) };
            button.PreviewMouseLeftButtonDown += (_, e) => { if (closed) return; automatic.IsChecked = false; host.SetButtonState(key, true); button.CaptureMouse(); e.Handled = true; };
            button.PreviewMouseLeftButtonUp += (_, e) => { if (!closed) host.SetButtonState(key, false); button.ReleaseMouseCapture(); e.Handled = true; };
            button.LostMouseCapture += (_, _) => { if (!closed) host.SetButtonState(key, false); };
            buttons.Children.Add(button);
        }
        controls.Children.Add(buttons); controls.Children.Add(decision); controls.Children.Add(board);
        var image = new System.Windows.Controls.Image { Source = lcd, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); root.Children.Add(image); Content = root;
        PreviewKeyDown += (_, e) => ManualKey(e, true); PreviewKeyUp += (_, e) => ManualKey(e, false);
        Deactivated += (_, _) => { if (!closed && automatic.IsChecked != true) host.ReleaseAll(); };
        timer.Tick += (_, _) => Tick();
        Closing += (_, _) =>
        {
            if (closed) return;
            closed = true; styleWindow?.Close(); timer.Stop(); lifetime.Cancel(); controller?.Stop(host);
            host.AttachSerialPeer(null); host.Dispose(); Shutdown = CloseAsync();
        };
        status.Text = "Load a matching ROM reader profile to enable AI. Manual play works without one.";
        if (DmgTetrisObserver.Supports(rom))
        {
            controller = new(new DmgTetrisObserver()); controller.Configure(style); automatic.IsEnabled = true;
            automatic.IsChecked = true;
            decision.Text = "Built-in reader verified. Select 2PLAYER on player one, then Start through music and height selection.";
        }
    }

    internal async Task ConnectAsync(string address, int port, bool tls, string? token)
    {
        var cartridge = new GB.Emulator.Core.Cartridge { Data = rom, Header = GB.Emulator.Core.Header.FromBytes(rom.AsSpan(0x100, 0x50).ToArray()) };
        host.Load(cartridge); host.Resume();
        var next = await LinkLobbyClient.ConnectAsync(address, port, "Tetris AI", cartridge.Header.Title,
            Convert.ToHexString(SHA256.HashData(rom)), lifetime.Token, tls, token, autoMatch: false);
        if (closed) { await next.DisposeAsync(); return; }
        client = next; client.WorkAvailable += QueueWake; previous = clock.Elapsed; timer.Start();
    }

    private void QueueWake()
    {
        if (lifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref wakeQueued, 1) != 0) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            { Interlocked.Exchange(ref wakeQueued, 0); if (!closed) Tick(present: false); });
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }

    private void Tick(bool present = true)
    {
        if (closed) return;
        try
        {
            var state = client?.State;
            var self = state?.Players.FirstOrDefault(p => p.Id == state.Self);
            // Only the player who explicitly added this opponent gets automatic consent.
            if (self?.InviteFrom == invitedBy && invitedBy != Guid.Empty && acceptedInvitation != invitedBy)
            { client!.Accept(invitedBy); acceptedInvitation = invitedBy; }
            if (self?.InviteFrom == Guid.Empty) acceptedInvitation = Guid.Empty;
            Guid session = client?.IsConnected == true ? state?.Session ?? Guid.Empty : Guid.Empty;
            if (attached != session)
            {
                controller?.Stop(host);
                if (attached != Guid.Empty && session == Guid.Empty) host.Pause();
                host.AttachSerialPeer(session == Guid.Empty ? null : client!.Peer); attached = session;
            }
            var now = clock.Elapsed; host.Advance(now - previous); previous = now;
            if (host.Fault != null) throw new InvalidOperationException("Player two emulator faulted.", host.Fault);
            if (automatic.IsChecked == true) controller?.Tick(host);
            if (!present) return;
            var frame = host.LatestFrame;
            if (frame != null && !ReferenceEquals(frame, presented))
            { lcd.WritePixels(new Int32Rect(0, 0, 160, 144), frame.Pixels.ToArray(), 160, 0); presented = frame; }
            status.Text = client?.Error ?? (session == Guid.Empty ? host.State == EmulationState.Paused ? "Cable disconnected. Reload the ROM to restart both players." : "In lobby. Invite ‘Tetris AI’ from player one's lobby." : $"Cable connected · {client!.BytesExchanged:N0} bytes · {client.RoundTripMilliseconds:F0} ms");
            var plan = controller?.Plan;
            decision.Text = automatic.IsChecked != true ? "AI off. Navigate to a two-player game, then enable AI." : plan == null ? controller?.IsThinking == true ? "Thinking about the current board and next piece…" : "AI ready. On player one, choose 2PLAYER, then Start through setup. Waiting for a playable board." :
                $"Target: {plan.Lines} lines · {plan.Holes} holes\nScore {plan.Score:F2} · next {plan.Moves.FirstOrDefault()}";
            var currentObservation = automatic.IsChecked == true ? controller?.Observation : null;
            var currentPlan = automatic.IsChecked == true ? plan : null;
            if (ReferenceEquals(drawnObservation, currentObservation) && ReferenceEquals(drawnPlan, currentPlan)) return;
            drawnObservation = currentObservation; drawnPlan = currentPlan; board.Children.Clear();
            if (automatic.IsChecked == true && controller?.Observation is { } observation)
                for (int i = 0; i < observation.Board.Length; i++)
                    if (observation.Board[i])
                    {
                        var cell = new System.Windows.Shapes.Rectangle { Width = 11, Height = 11, Fill = Brushes.SlateGray };
                        Canvas.SetLeft(cell, i % 10 * 12); Canvas.SetTop(cell, i / 10 * 12); board.Children.Add(cell);
                    }
            if (automatic.IsChecked == true && plan != null)
                foreach (var cell in plan.Landing)
                {
                    var marker = new System.Windows.Shapes.Rectangle { Width = 11, Height = 11, Fill = Brushes.YellowGreen };
                    Canvas.SetLeft(marker, cell.X * 12); Canvas.SetTop(marker, cell.Y * 12); board.Children.Add(marker);
                }
        }
        catch (Exception ex)
        {
            automatic.IsChecked = false; controller?.Stop(host); timer.Stop(); host.ReleaseAll(); status.Text = ex.Message;
        }
    }

    private void LoadProfile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Tetris ROM reader (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        automatic.IsChecked = false;
        try
        {
            var profile = TetrisMemoryProfile.Load(dialog.FileName, rom);
            controller?.Stop(host); controller = new(profile); controller.Configure(style); automatic.IsEnabled = true;
            decision.Text = "Profile verified against this ROM. Enable AI when both players are ready.";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException)
        { decision.Text = ex.Message; }
    }

    private void OpenStyle()
    {
        if (styleWindow != null) { styleWindow.Activate(); return; }
        styleWindow = new(style, ConfigureStyle) { Owner = this };
        styleWindow.Closed += (_, _) => styleWindow = null;
        styleWindow.Show();
    }

    private void ManualKey(KeyEventArgs e, bool pressed)
    {
        if (closed || automatic.IsChecked == true || e.IsRepeat) return;
        GameBoyButton? button = e.Key switch
        {
            Key.Left => GameBoyButton.Left, Key.Right => GameBoyButton.Right, Key.Down => GameBoyButton.Down, Key.Up => GameBoyButton.Up,
            Key.Z => GameBoyButton.A, Key.X => GameBoyButton.B, Key.Enter => GameBoyButton.Start, Key.Space => GameBoyButton.Select, _ => null
        };
        if (button is { } key) { host.SetButtonState(key, pressed); e.Handled = true; }
    }

    private async Task CloseAsync()
    {
        try { if (client != null) { client.WorkAvailable -= QueueWake; await client.DisposeAsync(); } }
        finally { lifetime.Dispose(); }
    }
}
