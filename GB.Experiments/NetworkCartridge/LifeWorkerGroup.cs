using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GB.Emulator.Core;
using GB.Emulator.Hosting;

namespace GB.Experiments.NetworkCartridge;

internal sealed record LifeWorkerAssignment(ushort NodeId, ushort Port);
internal sealed record LifeWorkerLaunch(LifeWorkerAssignment[] Nodes, bool Frames, bool Inspection, ushort[] DisplayNodes, string FirmwareSha256);
internal sealed record LifeWorkerResources(string Host, int ProcessId, long WorkingSetBytes, long PeakWorkingSetBytes, double CpuSeconds);
internal sealed record LifeWorkerEvent(string Kind, ushort NodeId = 0, NativeNodeSnapshot? Inspection = null,
    byte[]? Pixels = null, long FrameNumber = 0, long Cycles = 0, NativeNodeResult? Result = null, string? Error = null, LifeWorkerResources? Resources = null);

internal static class LifeWorkerProtocol
{
    internal static readonly string FirmwareSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(NativeNetworkNode.Firmware("Life")));
    internal static readonly JsonSerializerOptions Json = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(); options.Converters.Add(new LifeSupportConverter()); return options;
    }
    // Worker and coordinator run the same embedded Life ROM. Reassess its read-only hardware
    // capabilities locally instead of trying to deserialize the core's private support constructor.
    private sealed class LifeSupportConverter : JsonConverter<CartridgeSupport>
    {
        private static readonly Lazy<CartridgeSupport> Support = new(() =>
        {
            byte[] rom = NativeNetworkNode.Firmware("Life");
            return CartridgeSupport.Assess(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, Header.Length).ToArray()) });
        });
        public override CartridgeSupport Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.GetString() != "embedded-life") throw new JsonException("Unknown worker cartridge.");
            return Support.Value;
        }
        public override void Write(Utf8JsonWriter writer, CartridgeSupport value, JsonSerializerOptions options) => writer.WriteStringValue("embedded-life");
    }
}

/// <summary>A bounded group of independent native machines. Stdin EOF cancels all machines;
/// stdout carries detached inspection and selected LCDs, never simulation cell results.</summary>
public static class LifeWorkerGroup
{
    public static async Task<int> RunAsync()
    {
        Trace.Listeners.Clear();
        using var stop = new CancellationTokenSource();
        var output = Channel.CreateUnbounded<LifeWorkerEvent>(new() { SingleReader = true });
        Task? writer = null;
        Task resources = Task.CompletedTask;
        using var process = Process.GetCurrentProcess();
        void PublishResources()
        {
            process.Refresh();
            output.Writer.TryWrite(new("resources", Resources: new(Environment.MachineName, process.Id,
                process.WorkingSet64, process.PeakWorkingSet64, process.TotalProcessorTime.TotalSeconds)));
        }
        try
        {
            string first = await Console.In.ReadLineAsync() ?? throw new EndOfStreamException("Missing Life worker configuration.");
            var launch = JsonSerializer.Deserialize<LifeWorkerLaunch>(first) ?? throw new InvalidDataException("Invalid worker configuration.");
            if (launch.FirmwareSha256 != LifeWorkerProtocol.FirmwareSha256)
                throw new InvalidDataException("Life firmware differs between hosts. Deploy the same build to every worker host.");
            if (launch.Nodes.Length is < 1 or > 1000 || launch.Nodes.Any(n => n.NodeId is < 1 or > 1000 || n.Port == 0) ||
                launch.Nodes.Select(n => n.NodeId).Distinct().Count() != launch.Nodes.Length) throw new InvalidDataException("Invalid worker assignments.");
            ushort[] displayed = launch.DisplayNodes;
            writer = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in output.Reader.ReadAllAsync())
                    { await Console.Out.WriteLineAsync(JsonSerializer.Serialize(message, LifeWorkerProtocol.Json)); await Console.Out.FlushAsync(); }
                }
                catch { stop.Cancel(); throw; }
            });
            PublishResources();
            resources = Task.Run(async () =>
            {
                try { while (true) { await Task.Delay(1000, stop.Token); PublishResources(); } }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await Console.In.ReadLineAsync(stop.Token) is { } line)
                    {
                        var selected = JsonSerializer.Deserialize<ushort[]>(line) ?? [];
                        if (selected.Length > 128) throw new InvalidDataException("Too many displayed nodes in one process.");
                        Volatile.Write(ref displayed, selected);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                finally { stop.Cancel(); }
            });
            var tasks = launch.Nodes.Select(node => Task.Run(async () =>
            {
                try
                {
                    var result = await NativeNetworkNode.RunAsync("Life", node.NodeId, LocalNetworkTransport.Tcp, node.Port, stop.Token,
                        launch.Frames ? frame => output.Writer.TryWrite(new("frame", node.NodeId, Pixels: frame.Pixels.ToArray(), FrameNumber: frame.Number, Cycles: frame.Cycles)) : null,
                        TimeSpan.FromMinutes(10), onSnapshot: launch.Inspection ? snapshot => output.Writer.TryWrite(new("inspection", node.NodeId, Inspection: snapshot)) : null,
                        frameInterval: TimeSpan.FromMilliseconds(250), wantsFrames: () => Volatile.Read(ref displayed).Contains(node.NodeId),
                        networkTimeout: TimeSpan.FromMinutes(2), inspectionInterval: TimeSpan.FromSeconds(1), parkDuringNetworkIo: true);
                    if (result.Result != 1) throw new IOException($"GB {node.NodeId} failed with mailbox {result.Result}.");
                    output.Writer.TryWrite(new("result", node.NodeId, Result: result));
                }
                catch (Exception ex)
                { output.Writer.TryWrite(new("error", node.NodeId, Error: ex.Message)); stop.Cancel(); throw; }
            }, stop.Token)).ToArray();
            try { await Task.WhenAll(tasks); }
            finally { stop.Cancel(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally
        {
            stop.Cancel(); await resources; PublishResources(); output.Writer.TryWrite(new("drained")); output.Writer.TryComplete();
            if (writer != null) { try { await writer; } catch { } }
        }
    }
}

internal sealed class LifeProcessPool : IAsyncDisposable
{
    private readonly List<Group> groups = new();
    private readonly CancellationTokenSource controls = new();
    private Task controlTask = Task.CompletedTask;
    internal Task<NativeNodeResult>[] Nodes { get; }
    internal int Count => groups.Count;
    internal long WorkingSetBytes => groups.Sum(g => g.WorkingSetBytes);
    internal long PeakWorkingSetBytes => groups.Sum(g => g.PeakWorkingSetBytes);
    internal double CpuSeconds => groups.Sum(g => g.CpuSeconds);
    internal int[] ProcessIds => groups.Where(g => !g.Remote).Select(g => g.ProcessId).ToArray();
    internal LifeWorkerLocation[] Locations => groups.Select(g => g.Location).ToArray();
    internal Task DrainAsync(CancellationToken token) => Task.WhenAll(groups.Select(g => g.DrainAsync(token)));
    internal Task CompleteAsync(CancellationToken token) => Task.WhenAll(groups.Select(g => g.CompleteAsync(token)));

    private LifeProcessPool(Task<NativeNodeResult>[] nodes) => Nodes = nodes;
    internal static async Task<LifeProcessPool> StartAsync(IReadOnlyList<LocalRecordListener> listeners, int processes,
        Action<ushort, VideoFrame>? onFrame, Action<NativeNodeSnapshot>? onSnapshot, Func<ushort, bool>? wantsFrames, CancellationToken token,
        LifeHostConfiguration? placement = null)
    {
        var completions = Enumerable.Range(0, listeners.Count).Select(_ => new TaskCompletionSource<NativeNodeResult>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var pool = new LifeProcessPool(completions.Select(c => c.Task).ToArray());
        try
        {
            int hostStart = 0;
            foreach (var host in placement?.Hosts ?? [new LifeHost(null, null, listeners.Count, processes)])
            {
                for (int groupIndex = 0; groupIndex < host.Processes; groupIndex++)
                {
                    int offset = host.Nodes * groupIndex / host.Processes;
                    int start = hostStart + offset, end = hostStart + host.Nodes * (groupIndex + 1) / host.Processes;
                    var assignments = Enumerable.Range(start, end - start).Select(i => new LifeWorkerAssignment((ushort)(i + 1), listeners[i].Port)).ToArray();
                    ushort[] selection = assignments.Where(n => onFrame != null && (wantsFrames?.Invoke(n.NodeId) ?? true)).Select(n => n.NodeId).Take(128).ToArray();
                    var group = new Group(assignments, completions, onFrame, onSnapshot, host, placement); pool.groups.Add(group);
                    await group.StartAsync(new(assignments, onFrame != null, onSnapshot != null, selection, LifeWorkerProtocol.FirmwareSha256), offset, token);
                }
                hostStart += host.Nodes;
            }
            pool.controlTask = Task.Run(async () =>
            {
                try
                {
                    while (!pool.controls.IsCancellationRequested)
                    {
                        foreach (var group in pool.groups)
                        {
                            ushort[] selection = group.Assignments.Where(n => onFrame != null && (wantsFrames?.Invoke(n.NodeId) ?? true)).Select(n => n.NodeId).Take(128).ToArray();
                            await group.SelectAsync(selection, pool.controls.Token);
                        }
                        await Task.Delay(250, pool.controls.Token);
                    }
                }
                catch (OperationCanceledException) when (pool.controls.IsCancellationRequested) { }
                catch (Exception ex) { foreach (var completion in completions) completion.TrySetException(ex); }
            });
            return pool;
        }
        catch { await pool.DisposeAsync(); throw; }
    }
    internal async Task StopAsync()
    {
        controls.Cancel(); try { await controlTask; } catch { }
        foreach (var group in groups) group.CloseInput();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { await DrainAsync(deadline.Token); } catch { /* Broken or unresponsive children use the disposal fallback. */ }
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await Task.WhenAll(groups.Select(g => g.DisposeAsync().AsTask())); controls.Dispose();
    }
    private sealed class Group : IAsyncDisposable
    {
        private readonly TaskCompletionSource<NativeNodeResult>[] completions;
        private readonly Action<ushort, VideoFrame>? onFrame;
        private readonly Action<NativeNodeSnapshot>? onSnapshot;
        private Process? process;
        private Task reader = Task.CompletedTask;
        private Task<string>? errors;
        private ushort[] selected = [];
        private long peak;
        private double cpu;
        private readonly LifeHost host;
        private readonly LifeHostConfiguration? configuration;
        private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Exception? readFailure;
        private LifeWorkerResources? resources;
        internal bool Remote => host.Destination != null;
        internal LifeWorkerLocation Location => new(resources?.Host ?? host.Destination ?? Environment.MachineName, resources?.ProcessId ?? (Remote ? 0 : ProcessId), Remote);
        internal LifeWorkerAssignment[] Assignments { get; }
        internal int ProcessId => process?.Id ?? 0;
        internal long WorkingSetBytes
        {
            get
            {
                if (Remote) { var sample = Volatile.Read(ref resources); return sample?.WorkingSetBytes ?? 0; }
                try { process!.Refresh(); long value = process.WorkingSet64; peak = Math.Max(peak, process.PeakWorkingSet64); cpu = process.TotalProcessorTime.TotalSeconds; return value; } catch (InvalidOperationException) { return 0; }
            }
        }
        internal long PeakWorkingSetBytes { get { _ = WorkingSetBytes; return Remote ? resources?.PeakWorkingSetBytes ?? 0 : peak; } }
        internal double CpuSeconds { get { _ = WorkingSetBytes; return Remote ? resources?.CpuSeconds ?? 0 : cpu; } }
        internal Group(LifeWorkerAssignment[] assignments, TaskCompletionSource<NativeNodeResult>[] completions,
            Action<ushort, VideoFrame>? onFrame, Action<NativeNodeSnapshot>? onSnapshot, LifeHost host, LifeHostConfiguration? configuration)
        { Assignments = assignments; this.completions = completions; this.onFrame = onFrame; this.onSnapshot = onSnapshot; this.host = host; this.configuration = configuration; }
        internal async Task StartAsync(LifeWorkerLaunch launch, int offset, CancellationToken token)
        {
            string assembly = typeof(LifeWorkerGroup).Assembly.Location;
            string appHost = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
            var start = new ProcessStartInfo(File.Exists(appHost) ? appHost : "dotnet")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (configuration?.PasswordEnvironmentVariable is { } credential) start.Environment.Remove(credential);
            if (!File.Exists(appHost)) start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--life-worker-group");
            if (Remote)
            {
                try { start = LifeHostConfiguration.CreateSshStart(host, Assignments, offset, configuration); }
                catch { drained.TrySetResult(); throw; }
                launch = launch with { Nodes = Assignments.Select((n, i) => n with { Port = (ushort)(host.FirstPort + offset + i) }).ToArray() };
            }
            try { process = Process.Start(start) ?? throw new IOException("Could not start a Life worker group."); }
            catch { drained.TrySetResult(); throw; } // Nothing was launched; cleanup need not wait for a drain event.
            process.StandardInput.AutoFlush = true;
            errors = process.StandardError.ReadToEndAsync();
            reader = ReadAsync(); selected = launch.DisplayNodes;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(launch).AsMemory(), token);
        }
        private async Task ReadAsync()
        {
            var owned = Assignments.Select(n => n.NodeId).ToHashSet();
            try
            {
                while (await process!.StandardOutput.ReadLineAsync() is { } line)
                {
                    var message = JsonSerializer.Deserialize<LifeWorkerEvent>(line, LifeWorkerProtocol.Json) ?? throw new InvalidDataException("Empty worker event.");
                    if (message.Kind == "drained") { drained.TrySetResult(); continue; }
                    if (message.Kind == "resources" && message.Resources is { } sample)
                    {
                        if (sample.ProcessId <= 0 || sample.WorkingSetBytes < 0 || sample.PeakWorkingSetBytes < 0 || !double.IsFinite(sample.CpuSeconds) || sample.CpuSeconds < 0)
                            throw new InvalidDataException("Invalid worker resource sample.");
                        Volatile.Write(ref resources, sample); continue;
                    }
                    if (!owned.Contains(message.NodeId)) throw new InvalidDataException("Worker published an unassigned node ID.");
                    switch (message.Kind)
                    {
                        case "inspection" when message.Inspection?.NodeId == message.NodeId: onSnapshot?.Invoke(message.Inspection); break;
                        case "frame" when message.Pixels != null: onFrame?.Invoke(message.NodeId, VideoFrame.FromPixels(message.Pixels, message.FrameNumber, message.Cycles)); break;
                        case "result" when message.Result != null: completions[message.NodeId - 1].TrySetResult(message.Result); break;
                        case "error":
                            var failure = new IOException($"GB {message.NodeId}: {message.Error}");
                            foreach (var node in Assignments) completions[node.NodeId - 1].TrySetException(failure);
                            break; // keep delivering final inspection while the group shuts down
                        default: throw new InvalidDataException("Invalid Life worker event.");
                    }
                }
                await process.WaitForExitAsync();
                _ = WorkingSetBytes;
                string error = errors == null ? "" : await errors;
                if (process.ExitCode != 0 || Assignments.Any(n => !completions[n.NodeId - 1].Task.IsCompletedSuccessfully))
                    throw new IOException($"Life process {process.Id} exited before its workers completed (exit {process.ExitCode}): {error}");
                if (!drained.Task.IsCompleted) throw new IOException("Life worker exited without draining its native machines; deploy the same build on every host.");
            }
            catch (Exception ex)
            {
                readFailure = ex;
                drained.TrySetException(ex);
                foreach (var node in Assignments) completions[node.NodeId - 1].TrySetException(ex);
                // Continue draining cancellation/final-frame events so a child can flush and exit gracefully.
                try { while (await process!.StandardOutput.ReadLineAsync() != null) { } } catch (IOException) { }
            }
        }
        internal async Task SelectAsync(ushort[] selection, CancellationToken token)
        {
            if (selection.AsSpan().SequenceEqual(selected) || process == null || process.HasExited) return;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(selection).AsMemory(), token); selected = selection;
        }
        internal void CloseInput() { try { process?.StandardInput.Close(); } catch (InvalidOperationException) { } }
        internal Task DrainAsync(CancellationToken token) => drained.Task.WaitAsync(token);
        internal async Task CompleteAsync(CancellationToken token)
        {
            await reader.WaitAsync(token);
            if (readFailure != null) throw new IOException("Life worker transport did not complete cleanly.", readFailure);
        }
        public async ValueTask DisposeAsync()
        {
            if (process == null) return;
            CloseInput();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await reader; if (errors != null) await errors; process.Dispose();
        }
    }
}
