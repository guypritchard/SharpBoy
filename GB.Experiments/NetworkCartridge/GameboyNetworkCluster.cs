using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace GB.Experiments.NetworkCartridge;

public sealed record ClusterShard(byte NodeId, ushort JobId, ushort Start, ushort Count, ushort Sum);
public sealed record ClusterResult(int Workers, LocalNetworkTransport Transport, ushort Count, ushort Sum,
    IReadOnlyList<ClusterShard> Shards, IReadOnlyDictionary<byte, long> WorkerCycles);

/// <summary>Independent SM83 machines compute range reductions and exchange fixed-size network records.</summary>
public static class GameboyNetworkCluster
{
    public static async Task<ClusterResult> RunAsync(int workers = 4, ushort count = 1000,
        LocalNetworkTransport transport = LocalNetworkTransport.Tcp, int jobsPerWorker = 4,
        int tcpFragmentSize = 16, CancellationToken token = default)
    {
        if (workers is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(workers));
        if (jobsPerWorker is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(jobsPerWorker));
        if (tcpFragmentSize is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(tcpFragmentSize));
        if (!Enum.IsDefined(transport)) throw new ArgumentOutOfRangeException(nameof(transport));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromSeconds(30));
        var shards = new ConcurrentBag<ClusterShard>();
        var cycles = new ConcurrentDictionary<byte, long>();
        Task[] tasks = Enumerable.Range(0, workers).Select(index => Task.Run(async () =>
        {
            using var listener = new LocalRecordListener(transport);
            byte id = (byte)(index + 1);
            Task<NativeNodeResult> node = NativeNetworkNode.RunAsync("Worker", id, transport, listener.Port, lifetime.Token);
            try
            {
                byte[] hello = await listener.ReceiveAsync(16, lifetime.Token);
                if (!Valid(hello, id, 0)) throw new InvalidDataException("Invalid worker HELLO.");
                for (int round = 0; round < jobsPerWorker; round++)
                {
                    int ordinal = round * workers + index, jobs = workers * jobsPerWorker;
                    ushort start = (ushort)((long)count * ordinal / jobs);
                    ushort end = (ushort)((long)count * (ordinal + 1) / jobs);
                    ushort size = (ushort)(end - start), job = (ushort)(ordinal + 1);
                    byte[] request = Record(id, 1, job, start, size);
                    byte[]? reply = null;
                    // UDP retries are bounded. Range sums are deterministic, so replaying
                    // a job is harmless; stale/duplicate replies cannot complete another job.
                    for (int retry = 0; retry < 5 && reply == null; retry++)
                    {
                        await listener.SendAsync(request, tcpFragmentSize, lifetime.Token);
                        using var receive = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        if (transport == LocalNetworkTransport.Udp) receive.CancelAfter(TimeSpan.FromSeconds(2));
                        try
                        {
                            while (true)
                            {
                                byte[] candidate = await listener.ReceiveAsync(16, receive.Token);
                                if (!Valid(candidate, id, 2)) throw new InvalidDataException("Invalid worker result.");
                                if (BinaryPrimitives.ReadUInt16LittleEndian(candidate.AsSpan(6)) != job) continue;
                                if (!candidate.AsSpan(8, 4).SequenceEqual(request.AsSpan(8, 4))) throw new InvalidDataException("Worker operands changed.");
                                reply = candidate; break;
                            }
                        }
                        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested && transport == LocalNetworkTransport.Udp) { }
                    }
                    if (reply == null) throw new TimeoutException($"Worker {id} did not return job {job}.");
                    ushort sum = BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(12));
                    ushort expected = unchecked((ushort)((long)size * (2L * start + size - 1) / 2));
                    if (sum != expected) throw new InvalidDataException($"Worker {id} returned an incorrect native sum.");
                    shards.Add(new(id, job, start, size, sum));
                }
                await listener.SendAsync(Record(id, 3, 0, 0, 0), tcpFragmentSize, lifetime.Token);
                NativeNodeResult result = await node;
                if (result.Result != 1) throw new IOException($"Worker {id} failed with guest result {result.Result}.");
                cycles[id] = result.Cycles;
            }
            catch { lifetime.Cancel(); throw; }
            finally { try { await node; } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { } }
        }, lifetime.Token)).ToArray();
        await Task.WhenAll(tasks);
        ushort aggregate = unchecked((ushort)shards.Sum(s => s.Sum));
        ushort oracle = unchecked((ushort)((long)count * (count - 1) / 2));
        if (aggregate != oracle) throw new InvalidDataException("Cluster aggregate does not match the range sum.");
        return new(workers, transport, count, aggregate, shards.OrderBy(s => s.JobId).ToArray(), cycles);
    }
    private static bool Valid(byte[] bytes, byte id, byte kind) => bytes.Length == 16 &&
        bytes.AsSpan(0, 4).SequenceEqual("GBW1"u8) && bytes[4] == id && bytes[5] == kind;
    private static byte[] Record(byte id, byte kind, ushort job, ushort start, ushort count)
    {
        byte[] record = new byte[16]; "GBW1"u8.CopyTo(record); record[4] = id; record[5] = kind;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), job);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), start);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), count);
        return record;
    }
}
