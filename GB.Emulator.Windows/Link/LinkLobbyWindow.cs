using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using GB.Emulator.Hosting;
using GB.Emulator.Link;
using GB.Emulator.Windows.Desktop;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace GB.Emulator.Windows.Link;

/// <summary>Modeless lobby. UI thread owns host attachment; network code publishes detached state.</summary>
internal sealed class LinkLobbyWindow : Window
{
    private readonly EmulationHost host;
    private readonly Action<LinkActivitySnapshot>? visualStatus;
    private readonly Action? advance;
    private int wakeQueued;
    private readonly TextBox address = Field("127.0.0.1", 170), port = Field("8766", 65), playerName = Field("Player", 125);
    private readonly System.Windows.Controls.CheckBox tls = new() { Content = "TLS", Foreground = Brush("#E6E5D8"), Margin = new Thickness(8, 25, 8, 0) };
    private readonly PasswordBox accessToken = new() { Width = 125, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 5, 10, 0), Background = Brush("#29312C"), Foreground = Brush("#E6E5D8") };
    private readonly TextBlock gameTitle = Text("INSERT A CARTRIDGE", 28, true), status = Text("Choose a server, or host a lobby on this PC.", 13);
    private readonly TextBlock numbers = Text("", 12), invitation = Text("", 14, true);
    private readonly StackPanel players = new(), rooms = new();
    private readonly Button connect = Action("Connect"), start = Action("Host on this PC"), accept = Action("Accept cable"), decline = Action("Decline"), unplug = Action("Unplug cable"), disconnect = Action("Leave lobby");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Button addOpponent = Action("Random server opponent");
    private TetrisOpponentWindow? opponent;
    private readonly CancellationTokenSource lifetime = new();
    private readonly LinkCableModel hub = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private LinkLobbyClient? client;
    private LinkLobbyServer? server;
    private LinkLobbyState? rendered;
    private Guid attached;
    private bool busy, closed;
    private bool hasRendered;
    private string game = "", hash = "";
    private string shareAddress = "";
    private string connectedAddress = "";
    private int connectedPort;
    private bool connectedTls;
    private string? connectedToken;
    private sealed record ConnectionRequest(string Address, int Port, string Name, string Game, string Hash, bool Tls, string? Token, bool AutoMatch);
    private ConnectionRequest? reconnect;
    private CancellationTokenSource? connectionAttempt;
    private double retryAt = double.PositiveInfinity;
    private int retryCount;
    private readonly AxisAngleRotation3D rotation = new(new Vector3D(0, 1, 0), -16);
    private System.Windows.Point? drag;
    internal Task Shutdown { get; private set; } = Task.CompletedTask;
    internal LinkLobbyClient? Client => client;
    internal TetrisOpponentWindow? Opponent => opponent;
    internal string ConnectionStatus => status.Text;
    internal bool IsPlayerWindowActive => IsActive || opponent?.IsActive == true;
    internal bool HasConnectedCable => client?.Peer.IsConnected == true;

    internal LinkLobbyWindow(EmulationHost host, Action<LinkActivitySnapshot>? visualStatus = null, Action? advance = null)
    {
        this.host = host; this.visualStatus = visualStatus; this.advance = advance;
        Title = "SharpBoy · Link lounge"; Width = 1080; Height = 720; MinWidth = 940; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#171B19"); Foreground = Brush("#E6E5D8"); FontFamily = new("Segoe UI");
        var root = new Grid { Margin = new Thickness(26), Background = Background }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(Text("SHARPBOY  /  LINK LOUNGE", 12, true)); gameTitle.Margin = new Thickness(0, 8, 0, 5); header.Children.Add(gameTitle);
        header.Children.Add(Text("One cartridge. A room full of players. Connect your cable to someone ready to play.", 13));
        var settings = new WrapPanel { Margin = new Thickness(0, 18, 0, 20) };
        foreach (var (label, box) in new[] { ("SERVER", address), ("PORT", port), ("YOUR NAME", playerName) })
        {
            var field = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; field.Children.Add(Text(label, 10, true)); field.Children.Add(box); settings.Children.Add(field);
        }
        var secretField = new StackPanel(); secretField.Children.Add(Text("JOIN TOKEN (OPTIONAL)", 10, true)); secretField.Children.Add(accessToken);
        settings.Children.Add(tls); settings.Children.Add(secretField); settings.Children.Add(connect); settings.Children.Add(start); header.Children.Add(settings); root.Children.Add(header);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(.85, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1.15, GridUnitType.Star) }); Grid.SetRow(body, 1); root.Children.Add(body);
        var hardware = new Grid { Background = Brush("#202723"), Margin = new Thickness(0, 0, 20, 0) };
        hardware.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); hardware.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var view = new Viewport3D { ClipToBounds = true };
        view.Camera = new PerspectiveCamera(new Point3D(127, 23, 124), new Vector3D(0, -33, -124), new Vector3D(0, 1, 0), 39);
        var lights = new Model3DGroup(); lights.Children.Add(new AmbientLight(Color.FromRgb(75, 83, 79))); lights.Children.Add(new DirectionalLight(Color.FromRgb(220, 215, 189), new Vector3D(-1, -1, -2))); lights.Children.Add(new DirectionalLight(Color.FromRgb(88, 124, 119), new Vector3D(1, 0, -1)));
        view.Children.Add(new ModelVisual3D { Content = lights });
        hub.Hub.Transform = new RotateTransform3D(rotation, new Point3D(127, -10, 0)); view.Children.Add(new ModelVisual3D { Content = hub.Hub });
        view.MouseLeftButtonDown += (_, e) => { drag = e.GetPosition(view); view.CaptureMouse(); };
        view.MouseMove += (_, e) => { if (drag is { } from) { var to = e.GetPosition(view); rotation.Angle += (to.X - from.X) * .55; drag = to; } };
        view.MouseLeftButtonUp += (_, _) => { drag = null; view.ReleaseMouseCapture(); };
        hardware.Children.Add(view);
        var caption = new StackPanel { Margin = new Thickness(22, 0, 22, 24) }; caption.Children.Add(Text("YOUR LINK ACTIVITY MONITOR", 12, true));
        caption.Children.Add(Text("A miniature four-shade LCD shows read/write rates and 36 seconds of cable history. The LINK lamp lights when paired. Drag the monitor to turn it.", 13));
        numbers.Margin = new Thickness(0, 14, 0, 0); caption.Children.Add(numbers); Grid.SetRow(caption, 1); hardware.Children.Add(caption); body.Children.Add(hardware);
        var social = new StackPanel(); Grid.SetColumn(social, 1); body.Children.Add(new ScrollViewer { Content = social, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(body.Children[^1], 1);
        social.Children.Add(Text("PLAYERS IN YOUR CARTRIDGE LOBBY", 12, true)); players.Margin = new Thickness(0, 12, 0, 20); social.Children.Add(players);
        social.Children.Add(invitation); var choices = new WrapPanel(); choices.Children.Add(accept); choices.Children.Add(decline); social.Children.Add(choices);
        var cableActions = new WrapPanel(); cableActions.Children.Add(unplug); cableActions.Children.Add(disconnect); cableActions.Children.Add(addOpponent); social.Children.Add(cableActions);
        social.Children.Add(Text("CARTRIDGE ROOMS ON THIS HUB", 11, true)); social.Children.Add(rooms);
        status.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        connect.Click += async (_, _) => await ConnectAsync(false);
        start.Click += async (_, _) => await ConnectAsync(true);
        accept.Click += (_, _) => { if (Self()?.InviteFrom is { } id && id != Guid.Empty) client?.Accept(id); };
        decline.Click += (_, _) => { if (Self()?.InviteFrom is { } id && id != Guid.Empty) client?.Decline(id); };
        unplug.Click += (_, _) => DisconnectCable(); disconnect.Click += async (_, _) => await LeaveAsync();
        addOpponent.Click += (_, _) => client?.FindOpponent();
        timer.Tick += (_, _) => { Pump(); if (!busy && reconnect != null && clock.Elapsed.TotalSeconds >= retryAt) _ = RetryAsync(); };
        Loaded += (_, _) => { RefreshCartridge(); timer.Start(); Pump(); };
        Closing += (_, _) =>
        {
            if (closed) return; closed = true; timer.Stop(); lifetime.Cancel();
            if (host.State != EmulationState.Disposed) host.AttachSerialPeer(null);
            visualStatus?.Invoke(new(false, false, 0, 0, clock.Elapsed.TotalSeconds)); Shutdown = CloseAsync();
        };
    }
    private LobbyPlayer? Self() => client?.State?.Players.FirstOrDefault(p => p.Id == client.State.Self);
    internal void RefreshCartridge()
    {
        var cartridge = host.Inspection.CaptureCartridge(); game = cartridge?.Title.Trim('\0', ' ') ?? "";
        if (cartridge != null && game.Length == 0) game = "UNTITLED";
        gameTitle.Text = game.Length == 0 ? "INSERT A CARTRIDGE" : game;
        hash = cartridge?.Source is { } source && File.Exists(source) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) : new string('0', 64);
        rendered = null; hasRendered = false; Pump();
    }
    internal void DisconnectCable() { client?.Unlink(); if (host.State != EmulationState.Disposed) host.AttachSerialPeer(null); attached = Guid.Empty; }
    internal async Task LeaveAsync()
    {
        reconnect = null; retryAt = double.PositiveInfinity; retryCount = 0; connectionAttempt?.Cancel();
        if (opponent != null) { var oldOpponent = opponent; opponent = null; oldOpponent.Close(); await oldOpponent.Shutdown; }
        var old = client; client = null; attached = Guid.Empty;
        if (old != null) old.WorkAvailable -= QueueWake;
        if (host.State != EmulationState.Disposed) host.AttachSerialPeer(null);
        if (old != null) await old.DisposeAsync(); rendered = null; hasRendered = false; if (!closed) { status.Text = server == null ? "Disconnected from the lobby." : "Your lobby server is still running. Other players can continue to use it."; Pump(); }
    }
    private async Task ConnectAsync(bool hosting, bool autoMatch = true)
    {
        if (busy || closed) return; busy = true; Pump();
        try
        {
            if (!int.TryParse(port.Text, out int number) || number is < 1 or > 65535) throw new ArgumentException("Choose a TCP port from 1 to 65535.");
            if (hosting && server == null)
            {
                server = new LinkLobbyServer(IPAddress.Any, number);
                var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(n => n.Address)
                    .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    .OrderByDescending(ip => ip.GetAddressBytes()[0] is 192 or 10).Take(2);
                shareAddress = string.Join(" / ", addresses.Select(ip => $"{ip}:{number}"));
            }
            if (game.Length == 0) { status.Text = "Lobby server is ready. Insert a cartridge to join a game room."; return; }
            await LeaveAsync();
            status.Text = "Connecting to the link hub…";
            string endpoint = hosting ? "127.0.0.1" : address.Text.Trim();
            bool secure = !hosting && tls.IsChecked == true;
            string? secret = string.IsNullOrEmpty(accessToken.Password) ? null : accessToken.Password;
            reconnect = new(endpoint, number, playerName.Text.Trim(), game, hash, secure, secret, autoMatch);
            await OpenConnectionAsync(reconnect);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException or System.Net.NetworkInformation.NetworkInformationException)
        { if (!closed) status.Text = "Could not connect: " + ex.Message; }
        finally { busy = false; if (!closed) Pump(); }
    }
    private async Task OpenConnectionAsync(ConnectionRequest request)
    {
        retryAt = double.PositiveInfinity;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        connectionAttempt = attempt;
        try
        {
            var old = client; client = null;
            if (old != null) { old.WorkAvailable -= QueueWake; await old.DisposeAsync(); }
            var next = await LinkLobbyClient.ConnectAsync(request.Address, request.Port, request.Name, request.Game, request.Hash, attempt.Token,
                useTls: request.Tls, accessToken: request.Token, autoMatch: request.AutoMatch);
            if (closed || !ReferenceEquals(reconnect, request)) { await next.DisposeAsync(); return; }
            client = next; client.WorkAvailable += QueueWake; rendered = null; retryCount = 0;
            connectedAddress = request.Address; connectedPort = request.Port; connectedTls = request.Tls; connectedToken = request.Token;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            if (!closed && ReferenceEquals(reconnect, request)) ScheduleRetry(ex.Message);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.Authentication.AuthenticationException)
        {
            if (!closed && ReferenceEquals(reconnect, request)) { reconnect = null; status.Text = "Could not connect: " + ex.Message; }
        }
        finally { if (ReferenceEquals(connectionAttempt, attempt)) connectionAttempt = null; }
    }
    private void ScheduleRetry(string? error)
    {
        double delay = Math.Min(15, Math.Pow(2, Math.Min(retryCount++, 4)));
        retryAt = clock.Elapsed.TotalSeconds + delay;
        status.Text = $"Server disconnected. Retrying in {delay:0}s… {error}";
    }
    private async Task RetryAsync()
    {
        if (closed || busy || reconnect is not { } request) return;
        busy = true;
        try { status.Text = "Reconnecting to the link hub…"; await OpenConnectionAsync(request); }
        finally { busy = false; if (!closed) Pump(); }
    }
    internal Task JoinAsync(string serverAddress, int serverPort, string name, bool autoMatch = true)
    {
        address.Text = serverAddress; port.Text = serverPort.ToString(); playerName.Text = name;
        return ConnectAsync(false, autoMatch);
    }
    internal async Task AddOpponentAsync()
    {
        if (opponent != null) { opponent.Activate(); return; }
        if (client?.IsConnected != true || client.State == null) return;
        if (busy || closed) return;
        Guid ownerId = client.State.Self;
        string? path = host.Inspection.CaptureCartridge()?.Source;
        if (path == null || !File.Exists(path)) { status.Text = "Load a Tetris ROM from a file first."; return; }
        TetrisOpponentWindow? next = null;
        busy = true;
        try
        {
            byte[] rom = await File.ReadAllBytesAsync(path, lifetime.Token);
            if (closed || client?.State?.Self != ownerId) return;
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(rom)), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The ROM file changed since it was loaded. Reload it before adding an opponent.");
            next = new TetrisOpponentWindow(rom, ownerId);
            opponent = next;
            next.Closed += (_, _) => { if (ReferenceEquals(opponent, next)) opponent = null; };
            next.Show();
            await next.ConnectAsync(connectedAddress, connectedPort, connectedTls, connectedToken);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        { next?.Close(); status.Text = "Could not add opponent: " + ex.Message; }
        finally { busy = false; }
    }
    internal void Pump(bool present = true)
    {
        if (closed) return;
        bool online = client?.IsConnected == true; var state = online ? client!.State : null;
        Guid session = state?.Session ?? Guid.Empty;
        if (attached != session && host.State != EmulationState.Disposed)
        {
            if (attached != Guid.Empty && session == Guid.Empty) host.Pause();
            host.AttachSerialPeer(session == Guid.Empty ? null : client!.Peer); attached = session;
        }
        if (!present) return; // Per-byte wakeups attach the cable; the 120 ms UI timer owns presentation work.
        if (!online && !busy && reconnect != null && double.IsPositiveInfinity(retryAt)) ScheduleRetry(client?.Error);
        bool paired = session != Guid.Empty; long bytes = client?.BytesExchanged ?? 0;
        var traffic = client?.Traffic ?? default;
        var activity = new LinkActivitySnapshot(online, paired, traffic.BytesRead, traffic.BytesWritten, clock.Elapsed.TotalSeconds, state?.Self ?? Guid.Empty);
        visualStatus?.Invoke(activity);
        if (!IsVisible) return;
        hub.SetStatus(activity);
        addOpponent.IsEnabled = online && !busy && state?.Session == Guid.Empty && game.Contains("TETRIS", StringComparison.OrdinalIgnoreCase);
        connect.IsEnabled = !busy && game.Length > 0; start.IsEnabled = !busy && server == null; disconnect.IsEnabled = online || reconnect != null; unplug.IsEnabled = online && (paired || Self()?.InviteTo != Guid.Empty);
        accept.Visibility = decline.Visibility = Self()?.InviteFrom is { } from && from != Guid.Empty ? Visibility.Visible : Visibility.Collapsed;
        numbers.Text = online ? $"TCP · {client!.RoundTripMilliseconds:F0} ms round trip\n{bytes:N0} serial bytes exchanged\n{(paired ? "Cable connected" : "Connector available")}" : "TCP link relay · port " + port.Text + "\nNo ROM data is uploaded.";
        if (server != null) numbers.Text += "\nLAN: " + shareAddress;
        if (online) status.Text = client!.Error ?? (paired ? "Cable linked. Select 2PLAYER in your game; server opponents are ready automatically." : server != null ? "Hosting a relay on this PC. Headless opponents require the standalone server." : "Waiting for an available server opponent with the same ROM revision. You can also invite another human.");
        else if (client?.Error != null && reconnect == null) status.Text = client.Error;
        if (hasRendered && ReferenceEquals(rendered, state)) return; hasRendered = true; rendered = state; players.Children.Clear(); rooms.Children.Clear(); invitation.Text = "";
        if (state == null) { players.Children.Add(Text("Connect to see players. Each room is named from the loaded ROM header.", 14)); return; }
        foreach (var player in state.Players.Where(p => p.Game == LinkProtocolGame(game)))
        {
            bool me = player.Id == state.Self; var row = new Grid { Margin = new Thickness(0, 0, 0, 9), Background = Brush("#28322C") }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var words = new StackPanel { Margin = new Thickness(14, 12, 14, 12) }; words.Children.Add(Text(player.Name + (me ? "  /  YOU" : ""), 16, true));
            words.Children.Add(Text(player.Partner != Guid.Empty ? "CABLE CONNECTED" : player.InviteFrom != Guid.Empty || player.InviteTo != Guid.Empty ? "INVITATION PENDING" : "CONNECTOR AVAILABLE", 10)); row.Children.Add(words);
            if (!me && !player.Automated) { var button = Action("Link cable"); button.IsEnabled = !paired && player.Partner == Guid.Empty && player.InviteFrom == Guid.Empty && player.InviteTo == Guid.Empty && Self()?.InviteTo == Guid.Empty && Self()?.InviteFrom == Guid.Empty; button.Click += (_, _) => client?.Invite(player.Id); Grid.SetColumn(button, 1); row.Children.Add(button); }
            players.Children.Add(row);
        }
        if (Self()?.InviteFrom is { } inviter && inviter != Guid.Empty) invitation.Text = state.Players.FirstOrDefault(p => p.Id == inviter)?.Name + " would like to connect a cable.";
        foreach (var room in state.Players.GroupBy(p => p.Game).OrderBy(g => g.Key)) rooms.Children.Add(Text($"{room.Key}  /  {room.Count()} player{(room.Count() == 1 ? "" : "s")}", 13));
    }
    private void QueueWake()
    {
        if (lifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref wakeQueued, 1) != 0) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                Interlocked.Exchange(ref wakeQueued, 0);
                if (closed) return;
                Pump(present: false); advance?.Invoke();
            });
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
    private static string LinkProtocolGame(string title) => new string(title.Where(c => c is >= ' ' and <= '~').Take(16).ToArray()).Trim().ToUpperInvariant() is { Length: > 0 } key ? key : "UNTITLED";
    private async Task CloseAsync() { try { await LeaveAsync(); if (server != null) await server.DisposeAsync(); } finally { lifetime.Dispose(); } }
    internal void Capture(string path)
    {
        UpdateLayout(); var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render((Visual)Content);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var stream = File.Create(path); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(stream);
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock Text(string text, double size, bool bold = false) => new() { Text = text, FontSize = size, Foreground = Brush(bold ? "#E2DDC8" : "#A8B5A9"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
    private static TextBox Field(string text, double width) => new() { Text = text, Width = width, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 5, 0, 0), Background = Brush("#29312C"), Foreground = Brush("#E6E5D8"), BorderBrush = Brush("#536357") };
    private static Button Action(string text)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(5, 15, 0, 5), Background = Brush("#B0C49A"), Foreground = Brush("#19251D"), BorderThickness = new Thickness(0) };
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.3" /></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#CFDBB9" /></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        return button;
    }
}
