using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Threading.Channels;

namespace GB.Emulator.Link;

/// <summary>LAN rendezvous and byte relay. The hub owns pairings, never emulator state or ROMs.</summary>
public sealed class LinkLobbyServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private readonly Dictionary<Guid, Guest> guests = new();
    private readonly List<Task> connections = new();
    private readonly Task accepting;
    private int disposed;
    private readonly LinkServerOptions options;
    private readonly SslServerAuthenticationOptions? authentication;
    public event Action<Exception>? ClientError;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>Trusted service composition only: publish a ready headless player to the random pool.</summary>
    public void RegisterAutomatedPlayer(Guid id)
    {
        lock (gate)
        {
            if (!guests.TryGetValue(id, out var bot)) throw new InvalidOperationException("Player is no longer connected.");
            bot.Automated = true; bot.WantsMatch = false;
            foreach (var human in guests.Values.Where(g => !g.Automated && g.WantsMatch)) TryMatch(human);
            Broadcast();
        }
    }

    private void TryMatch(Guest human)
    {
        if (human.Automated || !Available(human)) return;
        var candidates = guests.Values.Where(g => g.Automated && Available(g) &&
            g.Game == human.Game && string.Equals(g.RomHash, human.RomHash, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0) return;
        Pair(human, candidates[System.Security.Cryptography.RandomNumberGenerator.GetInt32(candidates.Length)]);
    }
    private static bool Available(Guest guest) => !guest.Stop.IsCancellationRequested && guest.Partner == Guid.Empty && guest.InviteFrom == Guid.Empty && guest.InviteTo == Guid.Empty;
    private static void Pair(Guest a, Guest b)
    {
        a.Partner = b.Id; b.Partner = a.Id; a.Session = b.Session = Guid.NewGuid();
        a.Armed = b.Armed = null; a.Transfers = b.Transfers = 0; a.WantsMatch = b.WantsMatch = false;
    }

    public LinkLobbyServer(IPAddress? address = null, int port = 8766, LinkServerOptions? options = null)
    {
        this.options = options ?? new(); authentication = this.options.Authentication();
        listener = new(address ?? IPAddress.Loopback, port); listener.Start();
        accepting = Task.Run(AcceptAsync);
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(lifetime.Token); socket.NoDelay = true;
                lock (gate)
                {
                    connections.RemoveAll(t => t.IsCompleted);
                    if (connections.Count >= 40) { socket.Dispose(); continue; } // Includes bounded handshake/probe slots.
                    connections.Add(ServeAsync(socket));
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }
    private async Task ServeAsync(TcpClient socket)
    {
        using (socket)
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            Guest? guest = null; Task writing = Task.CompletedTask;
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); handshake.CancelAfter(TimeSpan.FromSeconds(10));
                using var tls = authentication == null ? null : new SslStream(socket.GetStream());
                Stream stream = tls == null ? socket.GetStream() : tls;
                if (tls != null) await tls.AuthenticateAsServerAsync(authentication!, handshake.Token);
                var hello = await LinkProtocol.ReadAsync(stream, handshake.Token);
                if (hello == null || !options.Accepts(hello.AccessToken)) throw new InvalidDataException("Invalid lobby access token.");
                if (hello.Kind == "probe") { await LinkProtocol.WriteAsync(stream, new("ok"), handshake.Token); return; }
                if (hello?.Kind != "hello" || string.IsNullOrWhiteSpace(hello.Game) || hello.Game.Length > 16 ||
                    string.IsNullOrWhiteSpace(hello.Name) || hello.Name.Length > 32 || hello.Name.Any(char.IsControl) ||
                    hello.RomHash?.Length != 64 || !hello.RomHash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid lobby introduction.");
                guest = new(hello.Name.Trim(), LinkProtocol.GameKey(hello.Game), hello.RomHash, stop) { WantsMatch = hello.AutoMatch };
                lock (gate) { if (guests.Count >= 32) throw new InvalidDataException("The lobby is full."); guests.Add(guest.Id, guest); if (guest.WantsMatch) TryMatch(guest); Broadcast(); }
                writing = WriteAsync(guest, stream, stop.Token);
                while (await LinkProtocol.ReadAsync(stream, stop.Token) is { } packet)
                    lock (gate) Handle(guest, packet);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException or ObjectDisposedException or System.Security.Authentication.AuthenticationException)
            { if (!stop.IsCancellationRequested) ClientError?.Invoke(ex); }
            finally
            {
                lock (gate)
                {
                    if (guest != null) { Unlink(guest); guests.Remove(guest.Id); guest.Output.Writer.TryComplete(); Broadcast(); }
                }
                stop.Cancel(); socket.Close();
                try { await writing; } catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidDataException or ObjectDisposedException) { }
            }
        }
    }
    private static async Task WriteAsync(Guest guest, Stream stream, CancellationToken token)
    {
        try { await foreach (var packet in guest.Output.Reader.ReadAllAsync(token)) await LinkProtocol.WriteAsync(stream, packet, token); }
        finally { guest.Stop.Cancel(); }
    }
    private void Handle(Guest guest, LinkPacket packet)
    {
        if (packet.Kind == "ping") { guest.Send(new("pong", Transfer: packet.Transfer)); return; }
        if (packet.Kind == "unlink") { Unlink(guest); Broadcast(); return; }
        if (packet.Kind == "match") { guest.WantsMatch = true; TryMatch(guest); Broadcast(); return; }
        if (packet.Kind == "cancel")
        {
            if (packet.Session == guest.Session && guest.Armed?.Transfer == packet.Transfer) guest.Armed = null;
            return;
        }
        if (packet.Kind == "arm")
        {
            if (guest.Session == Guid.Empty || packet.Session != guest.Session || !guests.TryGetValue(guest.Partner, out var other)) return;
            if (packet.Transfer <= guest.LastTransfer) throw new InvalidDataException("Out-of-order serial transfer.");
            guest.LastTransfer = packet.Transfer; guest.Armed = packet;
            if (other.Armed is { } armed && (packet.InternalClock || armed.InternalClock))
            {
                guest.Send(new("byte", Session: guest.Session, Transfer: packet.Transfer, Value: armed.Value));
                other.Send(new("byte", Session: guest.Session, Transfer: armed.Transfer, Value: packet.Value));
                guest.Armed = other.Armed = null; guest.Transfers++; other.Transfers++;
            }
            return;
        }
        if (!guests.TryGetValue(packet.Target, out var target) || target == guest || target.Game != guest.Game)
        { guest.Send(new("error", Error: "Choose a player in the same ROM-title lobby.")); return; }
        if (packet.Kind == "invite" && target.Automated)
        { guest.Send(new("error", Error: "Use random opponent matchmaking for server players.")); return; }
        switch (packet.Kind)
        {
            case "invite" when guest.Partner == Guid.Empty && target.Partner == Guid.Empty &&
                guest.InviteFrom == Guid.Empty && guest.InviteTo == Guid.Empty && target.InviteFrom == Guid.Empty && target.InviteTo == Guid.Empty:
                guest.InviteTo = target.Id; target.InviteFrom = guest.Id; Broadcast(); break;
            case "accept" when guest.InviteFrom == target.Id && target.InviteTo == guest.Id && guest.Partner == Guid.Empty && target.Partner == Guid.Empty:
                guest.InviteFrom = target.InviteTo = Guid.Empty;
                Pair(guest, target); Broadcast(); break;
            case "decline" when guest.InviteFrom == target.Id:
                guest.InviteFrom = target.InviteTo = Guid.Empty; Broadcast(); break;
            default: guest.Send(new("error", Error: "That connector is busy or the invitation has expired.")); break;
        }
    }
    private void Unlink(Guest guest)
    {
        foreach (var other in guests.Values)
        {
            if (other.Partner == guest.Id) { other.Partner = other.Session = Guid.Empty; other.Armed = null; other.WantsMatch = false; if (other.Automated) other.Stop.Cancel(); }
            if (other.InviteFrom == guest.Id) other.InviteFrom = Guid.Empty;
            if (other.InviteTo == guest.Id) other.InviteTo = Guid.Empty;
        }
        guest.Partner = guest.Session = guest.InviteFrom = guest.InviteTo = Guid.Empty; guest.Armed = null;
        guest.WantsMatch = false;
        if (guest.Automated) guest.Stop.Cancel();
    }
    private void Broadcast()
    {
        var players = guests.Values.Select(g => new LobbyPlayer(g.Id, g.Name, g.Game, g.RomHash, g.Partner, g.InviteFrom, g.InviteTo, g.Automated)).ToArray();
        foreach (var guest in guests.Values) guest.Send(new("state", State: new(guest.Id, guest.Session, players, guest.Transfers)));
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); listener.Stop(); await accepting;
        Task[] tasks; lock (gate) { foreach (var guest in guests.Values) guest.Stop.Cancel(); tasks = connections.ToArray(); }
        await Task.WhenAll(tasks); lifetime.Dispose();
    }
    private sealed class Guest(string name, string game, string hash, CancellationTokenSource stop)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal string Name { get; } = name;
        internal string Game { get; } = game;
        internal string RomHash { get; } = hash;
        internal CancellationTokenSource Stop { get; } = stop;
        internal Channel<LinkPacket> Output { get; } = Channel.CreateBounded<LinkPacket>(128);
        internal Guid Partner, Session, InviteFrom, InviteTo;
        internal bool Automated, WantsMatch;
        internal long LastTransfer, Transfers;
        internal LinkPacket? Armed;
        internal void Send(LinkPacket message) { if (!Output.Writer.TryWrite(message)) Stop.Cancel(); }
    }
}
