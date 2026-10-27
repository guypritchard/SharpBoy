using GB.Emulator.Core.InputOutput;

namespace GB.Experiments.NetworkCartridge;

public enum NetworkCommand : byte { Connect = 1, Send, Receive, Close, Acknowledge }
public enum NetworkError : byte { None, InvalidCommand, Busy, InvalidLength, NotConnected, Transport, ReceivePending, Cancelled, TimedOut }

/// <summary>
/// Experimental socket-offload cartridge. All register access and Pump calls belong to one execution context.
/// Async transports return detached results; they never modify registers from a worker continuation.
/// </summary>
public sealed class NetworkCartridgeDevice : IInspectableMemoryRange, IAsyncDisposable
{
    public const ushort Status = 0xA002, Command = 0xA003, Error = 0xA004;
    public const ushort TxLength = 0xA005, RxLength = 0xA007, TxBuffer = 0xA100, RxBuffer = 0xA200;
    public const int Capacity = 256;
    public const int DatagramCapacity = 1024;
    public const ushort LocalPort = 0xA010, RemotePort = 0xA012, RemoteAddress = 0xA014;
    public const ushort SourcePort = 0xA018, SourceAddress = 0xA01A;
    public const ushort Transport = 0xA00A, Bootstrap = 0xA030;
    public const byte Connected = 1, Busy = 2, ReceiveReady = 4, Failed = 8;
    private readonly INetworkCartridgeBackend? backend;
    private readonly IDatagramCartridgeBackend? datagrams;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan operationTimeout = TimeSpan.FromSeconds(5);
    private readonly byte[] tx, rx;
    private readonly byte[] endpoints = new byte[14];
    private Task<Completion>? operation;
    private Request? queued;
    private ushort txLength, rxLength;
    private bool connected, disposed, datagramReceived;
    private NetworkError error;
    private bool streamMode;
    private readonly byte[] bootstrap = new byte[16];
    private bool IsDatagram => this.datagrams != null && !this.streamMode;

    public NetworkCartridgeDevice(INetworkCartridgeBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.tx = new byte[Capacity]; this.rx = new byte[Capacity];
    }
    public NetworkCartridgeDevice(IDatagramCartridgeBackend backend)
    {
        this.datagrams = backend ?? throw new ArgumentNullException(nameof(backend));
        this.tx = new byte[DatagramCapacity]; this.rx = new byte[DatagramCapacity];
    }
    /// <summary>Version-2 buffers with optional TCP offload; switch transport only while closed.</summary>
    public NetworkCartridgeDevice(IDatagramCartridgeBackend datagrams, INetworkCartridgeBackend streams, ReadOnlySpan<byte> bootstrap, TimeSpan? operationTimeout = null)
        : this(datagrams)
    {
        this.backend = streams ?? throw new ArgumentNullException(nameof(streams));
        if (bootstrap.Length > this.bootstrap.Length) throw new ArgumentException("Bootstrap exceeds 16 bytes.");
        if (operationTimeout.HasValue)
        {
            if (operationTimeout.Value <= TimeSpan.Zero || operationTimeout.Value > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            this.operationTimeout = operationTimeout.Value;
        }
        bootstrap.CopyTo(this.bootstrap);
    }
    public int BufferCapacity => this.tx.Length;
    public ushort ReceiveBufferAddress => (ushort)(TxBuffer + this.BufferCapacity);
    public ushort Start => 0xA000;
    public ushort End => 0xBFFF;
    public bool HasPendingOperation => this.queued != null || this.operation != null;
    public byte Read8(ushort location) => this.Peek(location);

    public byte Peek(ushort address)
    {
        if (this.datagrams != null && address >= Bootstrap && address < Bootstrap + this.bootstrap.Length)
            return this.bootstrap[address - Bootstrap];
        if (address >= TxBuffer && address < TxBuffer + this.BufferCapacity) return this.tx[address - TxBuffer];
        if (address >= this.ReceiveBufferAddress && address < this.ReceiveBufferAddress + this.BufferCapacity) return this.rx[address - this.ReceiveBufferAddress];
        if (this.datagrams != null && address >= LocalPort && address < LocalPort + this.endpoints.Length)
            return this.endpoints[address - LocalPort];
        return address switch
        {
            0xA000 => (byte)'N', 0xA001 => this.datagrams == null ? (byte)1 : (byte)2,
            0xA009 => this.datagrams == null ? (byte)0 : this.backend == null ? (byte)1 : (byte)3,
            Transport => this.datagrams == null ? (byte)1 : this.streamMode ? (byte)1 : (byte)0,
            Status => (byte)((this.connected ? Connected : 0) | (this.HasPendingOperation ? Busy : 0) |
                (this.rxLength > 0 || this.datagramReceived ? ReceiveReady : 0) | (this.error != NetworkError.None ? Failed : 0)),
            Error => (byte)this.error,
            TxLength => (byte)this.txLength, TxLength + 1 => (byte)(this.txLength >> 8),
            RxLength => (byte)this.rxLength, RxLength + 1 => (byte)(this.rxLength >> 8),
            _ => 0xFF
        };
    }

    public void Write8(ushort location, byte value)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (location == Transport && this.datagrams != null && this.backend != null)
        {
            if (this.HasPendingOperation) this.error = NetworkError.Busy;
            else if (this.connected || value > 1 || this.datagramReceived || this.rxLength != 0) this.error = NetworkError.InvalidCommand;
            else { this.streamMode = value == 1; this.error = NetworkError.None; }
            return;
        }
        if (location >= TxBuffer && location < TxBuffer + this.BufferCapacity) this.tx[location - TxBuffer] = value;
        else if (this.datagrams != null && location >= LocalPort && location < SourcePort)
            this.endpoints[location - LocalPort] = value;
        else if (location == TxLength) this.txLength = (ushort)((this.txLength & 0xFF00) | value);
        else if (location == TxLength + 1) this.txLength = (ushort)((this.txLength & 0xFF) | value << 8);
        else if (location == Command) this.Submit((NetworkCommand)value);
    }

    private void Submit(NetworkCommand command)
    {
        if (this.HasPendingOperation) { this.error = NetworkError.Busy; return; }
        this.error = NetworkError.None;
        if (!Enum.IsDefined(command)) { this.error = NetworkError.InvalidCommand; return; }
        if (command == NetworkCommand.Acknowledge) { this.rxLength = 0; this.datagramReceived = false; Array.Clear(this.rx); return; }
        if (command is NetworkCommand.Send or NetworkCommand.Receive && !this.connected)
        { this.error = NetworkError.NotConnected; return; }
        if (command == NetworkCommand.Send && (this.txLength > this.BufferCapacity || (this.txLength == 0 && !this.IsDatagram)))
        { this.error = NetworkError.InvalidLength; return; }
        if (command == NetworkCommand.Receive && (this.rxLength != 0 || this.datagramReceived))
        { this.error = NetworkError.ReceivePending; return; }
        this.queued = new(command, command == NetworkCommand.Send ? this.tx.AsSpan(0, this.txLength).ToArray() : [],
            (ushort)(this.endpoints[0] | this.endpoints[1] << 8),
            new(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(this.endpoints.AsSpan(4, 4)),
                (ushort)(this.endpoints[2] | this.endpoints[3] << 8)));
    }

    /// <summary>Await transport work without publishing its result. Pump must still run on the owning context.</summary>
    public async ValueTask WaitForPendingOperationAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.operation is { } pending) await pending.WaitAsync(token);
    }

    /// <summary>Call between bounded CPU batches. Starting and finishing I/O never blocks a CPU memory access.</summary>
    public void Pump()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.queued is Request request)
        {
            this.queued = null;
            this.operation = this.ExecuteAsync(request);
        }
        if (this.operation?.IsCompleted != true) return;
        Completion result = this.operation.GetAwaiter().GetResult();
        this.operation = null;
        this.error = result.Error;
        if (result.Error != NetworkError.None)
        { if (!this.IsDatagram || result.Error != NetworkError.TimedOut || result.Command != NetworkCommand.Receive) this.connected = false; return; }
        if (result.Command == NetworkCommand.Connect)
        { this.connected = true; this.rxLength = 0; this.datagramReceived = false; Array.Clear(this.rx); }
        if (result.Command == NetworkCommand.Close) { this.connected = false; this.rxLength = 0; this.datagramReceived = false; Array.Clear(this.rx); }
        if (result.Command == NetworkCommand.Receive)
        {
            Array.Clear(this.rx);
            result.Bytes.CopyTo(this.rx, 0);
            this.rxLength = (ushort)result.Bytes.Length;
            if (this.IsDatagram)
            {
                this.datagramReceived = true;
                this.endpoints[8] = (byte)result.Source.Port; this.endpoints[9] = (byte)(result.Source.Port >> 8);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(this.endpoints.AsSpan(10, 4), result.Source.Address);
            }
            else if (this.rxLength == 0) this.connected = false; // TCP EOF; zero-length UDP is valid
        }
    }

    private async Task<Completion> ExecuteAsync(Request request)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
        timeout.CancelAfter(this.operationTimeout);
        try
        {
            byte[] received = [];
            DatagramEndpoint source = default;
            switch (request.Command)
            {
                case NetworkCommand.Connect:
                    if (this.IsDatagram) await this.datagrams!.OpenAsync(request.LocalPort, timeout.Token);
                    else await this.backend!.ConnectAsync(timeout.Token);
                    break;
                case NetworkCommand.Send:
                    if (this.IsDatagram) await this.datagrams!.SendAsync(request.Destination, request.Bytes, timeout.Token);
                    else await this.backend!.SendAsync(request.Bytes, timeout.Token);
                    break;
                case NetworkCommand.Receive:
                    if (this.IsDatagram)
                    {
                        NetworkDatagram packet = await this.datagrams!.ReceiveAsync(timeout.Token);
                        if (packet.Payload.Length > this.BufferCapacity) throw new IOException("Datagram exceeds the cartridge buffer.");
                        received = packet.Payload.ToArray(); source = packet.Source;
                    }
                    else
                    {
                        var buffer = new byte[this.BufferCapacity];
                        int count = await this.backend!.ReceiveAsync(buffer, timeout.Token);
                        if (count < 0 || count > this.BufferCapacity) throw new IOException("Backend returned an invalid receive count.");
                        received = buffer.AsSpan(0, count).ToArray();
                    }
                    break;
                case NetworkCommand.Close:
                    if (this.IsDatagram) await this.datagrams!.CloseAsync(timeout.Token);
                    else await this.backend!.CloseAsync(timeout.Token);
                    break;
            }
            return new(request.Command, received, NetworkError.None, source);
        }
        catch (TimeoutException) { return new(request.Command, [], NetworkError.TimedOut); }
        catch (OperationCanceledException)
        { return new(request.Command, [], this.IsDatagram && !this.lifetime.IsCancellationRequested ? NetworkError.TimedOut : NetworkError.Cancelled); }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        { return new(request.Command, [], NetworkError.Transport); }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.lifetime.Cancel();
        this.queued = null;
        try { if (this.operation != null) await this.operation; }
        finally
        {
            this.operation = null;
            this.connected = false;
            try
            {
                try { if (this.datagrams != null) await this.datagrams.DisposeAsync(); }
                finally { if (this.backend != null) await this.backend.DisposeAsync(); }
            }
            finally { this.lifetime.Dispose(); }
        }
    }

    private sealed record Request(NetworkCommand Command, byte[] Bytes, ushort LocalPort, DatagramEndpoint Destination);
    private sealed record Completion(NetworkCommand Command, byte[] Bytes, NetworkError Error, DatagramEndpoint Source = default);
}
