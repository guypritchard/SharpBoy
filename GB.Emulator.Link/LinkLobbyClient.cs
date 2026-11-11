using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Security;
using System.Threading.Channels;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Link;

/// <summary>Serial payload bytes only; excludes lobby JSON, heartbeats and transport framing.</summary>
public readonly record struct LinkTrafficStatistics(long BytesRead, long BytesWritten);

/// <summary>Immutable lobby publication and ordered TCP I/O; no access to emulated registers.</summary>
public sealed class LinkLobbyClient : IAsyncDisposable
{
    private readonly TcpClient socket = new() { NoDelay = true };
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<LinkPacket> output = Channel.CreateBounded<LinkPacket>(256);
    private LinkLobbyState? state;
    private string? error;
    private Task running = Task.CompletedTask;
    private int disposed;
    private double latency;
    private Stream? transport;
    public LinkLobbyState? State => Volatile.Read(ref state);
    public string? Error => Volatile.Read(ref error);
    public bool IsConnected => State != null && !stop.IsCancellationRequested;
    public double RoundTripMilliseconds => Volatile.Read(ref latency);
    public long BytesExchanged => Peer.CompletedTransfers;
    public LinkTrafficStatistics Traffic => Peer.Traffic;
    public NetworkSerialPeer Peer { get; }
    /// <summary>Background notification only. Marshal to the machine's owner before advancing it.</summary>
    public event Action? WorkAvailable;
    private LinkLobbyClient() => Peer = new(this);

    public static async Task<LinkLobbyClient> ConnectAsync(string host, int port, string name, string game, string romHash, CancellationToken token = default,
        bool useTls = false, string? accessToken = null, bool autoMatch = true)
    {
        var client = new LinkLobbyClient();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await client.socket.ConnectAsync(host, port, deadline.Token);
            client.transport = client.socket.GetStream();
            if (useTls)
            {
                var tls = new SslStream(client.transport); client.transport = tls;
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13 }, deadline.Token);
            }
            await LinkProtocol.WriteAsync(client.transport, new("hello", Name: name, Game: game, RomHash: romHash, AccessToken: accessToken, AutoMatch: autoMatch), deadline.Token);
            var initial = await LinkProtocol.ReadAsync(client.transport, deadline.Token);
            if (initial?.State == null) throw new IOException("The lobby did not accept this cartridge/player.");
            client.state = initial.State; client.Peer.UpdateSession(initial.State.Session);
            client.running = Task.Run(client.RunAsync); return client;
        }
        catch { await client.DisposeAsync(); throw; }
    }
    public void Invite(Guid player) { Volatile.Write(ref error, null); Send(new("invite", Target: player)); }
    public void FindOpponent() { Volatile.Write(ref error, null); Send(new("match")); }
    public void Accept(Guid player) { Volatile.Write(ref error, null); Send(new("accept", Target: player)); }
    public void Decline(Guid player) { Volatile.Write(ref error, null); Send(new("decline", Target: player)); }
    public void Unlink() { Volatile.Write(ref error, null); Peer.CancelTransfer(); Send(new("unlink")); }
    internal void Send(LinkPacket packet)
    {
        if (!IsConnected) return;
        if (!output.Writer.TryWrite(packet)) Fail("The link send queue is full; the cable was disconnected.");
    }
    private async Task RunAsync()
    {
        async Task Read()
        {
            while (await LinkProtocol.ReadAsync(transport!, stop.Token) is { } packet)
            {
                switch (packet.Kind)
                {
                    case "state" when packet.State != null:
                        Peer.UpdateSession(packet.State.Session); Volatile.Write(ref state, packet.State); WorkAvailable?.Invoke(); break;
                    case "byte": Peer.Receive(packet); break;
                    case "error": Volatile.Write(ref error, packet.Error); break;
                    case "pong": Volatile.Write(ref latency, Stopwatch.GetElapsedTime(packet.Transfer).TotalMilliseconds); break;
                    default: throw new InvalidDataException("Unexpected lobby packet.");
                }
            }
            throw new EndOfStreamException("The lobby server disconnected.");
        }
        async Task Write()
        {
            await foreach (var packet in output.Reader.ReadAllAsync(stop.Token))
                await LinkProtocol.WriteAsync(transport!, packet, stop.Token);
        }
        async Task Heartbeat()
        {
            while (true)
            {
                Send(new("ping", Transfer: Stopwatch.GetTimestamp()));
                Peer.CheckTimeout();
                await Task.Delay(1000, stop.Token);
            }
        }
        Task[] tasks = [Read(), Write(), Heartbeat()];
        try { await await Task.WhenAny(tasks); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException or ObjectDisposedException)
        { if (!stop.IsCancellationRequested) Volatile.Write(ref error, ex.Message); }
        finally
        {
            stop.Cancel(); socket.Dispose(); transport?.Dispose(); output.Writer.TryComplete(); Peer.UpdateSession(Guid.Empty);
            foreach (var task in tasks) { try { await task; } catch { /* Every transport task is observed at shutdown. */ } }
        }
    }
    private void Fail(string message) { Volatile.Write(ref error, message); stop.Cancel(); socket.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel(); socket.Dispose(); transport?.Dispose(); output.Writer.TryComplete(); await running; stop.Dispose();
    }

    public sealed class NetworkSerialPeer : IDeferredSerialPeer
    {
        private readonly LinkLobbyClient client;
        private readonly object gate = new();
        private Guid session;
        private long sequence, pending, started, completed, bytesRead, bytesWritten;
        private byte? received;
        private bool waitingForInternalClock;
        internal NetworkSerialPeer(LinkLobbyClient client) => this.client = client;
        public bool IsConnected { get { lock (gate) return session != Guid.Empty && client.IsConnected; } }
        public bool IsWaiting { get { lock (gate) return pending != 0 && received == null && session != Guid.Empty; } }
        public long CompletedTransfers => Interlocked.Read(ref completed);
        public LinkTrafficStatistics Traffic { get { lock (gate) return new(bytesRead, bytesWritten); } }
        public byte ExchangeByte(byte outgoing) => throw new InvalidOperationException("Deferred cable transfers must be polled by the serial port.");
        public void BeginTransfer(byte outgoing, bool internalClock)
        {
            lock (gate)
            {
                if (session == Guid.Empty) return;
                pending = ++sequence; received = null; started = Stopwatch.GetTimestamp(); waitingForInternalClock = internalClock;
                client.Send(new("arm", Session: session, Transfer: pending, Value: outgoing, InternalClock: internalClock));
            }
        }
        public bool TryCompleteTransfer(out byte incoming)
        {
            lock (gate)
            {
                incoming = received.GetValueOrDefault();
                if (!received.HasValue) return false;
                pending = 0; received = null; Interlocked.Increment(ref completed); return true;
            }
        }
        public void CancelTransfer()
        {
            lock (gate)
            {
                if (pending != 0) client.Send(new("cancel", Session: session, Transfer: pending));
                pending = 0; received = null;
            }
        }
        internal void UpdateSession(Guid next)
        {
            lock (gate) { if (next == session) return; session = next; pending = 0; received = null; }
        }
        internal void Receive(LinkPacket packet)
        {
            bool ready = false;
            lock (gate)
            {
                if (pending != 0 && received == null && packet.Session == session && packet.Transfer == pending)
                {
                    // A successful rendezvous shifts one byte in each direction. Count the
                    // acknowledged payload, not external-clock re-arms which can be cancelled.
                    received = packet.Value; bytesRead++; bytesWritten++; ready = true;
                }
            }
            if (ready) client.WorkAvailable?.Invoke();
        }
        internal void CheckTimeout()
        {
            lock (gate)
                if (pending != 0 && waitingForInternalClock && received == null && Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(15))
                {
                    Volatile.Write(ref client.error, "The other Game Boy did not supply a serial transfer/clock within 15 seconds. Reconnect the cable when both games are ready.");
                    pending = 0; client.Send(new("unlink")); session = Guid.Empty;
                }
        }
    }
}
