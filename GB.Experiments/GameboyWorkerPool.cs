using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GB.Experiments;

/// <summary>Round-robin pool of isolated Game Boy emulator processes.</summary>
public sealed class GameboyWorkerPool : ISerialRequestHandler, IAsyncDisposable
{
    private readonly Worker[] workers;
    private long nextWorker = -1;

    public GameboyWorkerPool(int count, string? romPath = null)
    {
        if (count is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(count));
        string? fullRomPath = romPath is null ? null : Path.GetFullPath(romPath);
        this.workers = new Worker[count];
        try
        {
            for (int i = 0; i < count; i++) this.workers[i] = new Worker(fullRomPath);
        }
        catch
        {
            foreach (Worker? worker in this.workers) worker?.Stop();
            throw;
        }
    }

    public int Count => this.workers.Length;
    public IReadOnlyList<long> AssignmentCounts => this.workers.Select(worker => worker.AssignmentCount).ToArray();
    public long TotalWorkerWorkingSetBytes => this.workers.Sum(worker => worker.WorkingSetBytes);

    public async Task<string> GetPageAsync(CancellationToken cancellationToken)
    {
        string reply = await this.SelectWorker().ExchangeAsync("G", cancellationToken);
        if (!reply.StartsWith("G ", StringComparison.Ordinal)) throw WorkerFailure(reply);
        return Encoding.UTF8.GetString(Convert.FromBase64String(reply[2..]));
    }

    public async Task<int> GetPrimeAsync(int index, CancellationToken cancellationToken)
    {
        if (index is < 1 or > DemoRom.MaximumPrimeIndex)
            throw new ArgumentOutOfRangeException(nameof(index));
        string reply = await this.SelectWorker().ExchangeAsync($"P {index}", cancellationToken);
        if (!reply.StartsWith("P ", StringComparison.Ordinal)
            || !int.TryParse(reply.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out int prime))
            throw WorkerFailure(reply);
        return prime;
    }

    private Worker SelectWorker()
    {
        long index = Interlocked.Increment(ref this.nextWorker);
        return this.workers[(int)(index % this.workers.Length)];
    }

    private static Exception WorkerFailure(string reply) =>
        reply.StartsWith("E ", StringComparison.Ordinal)
            ? new InvalidOperationException(Encoding.UTF8.GetString(Convert.FromBase64String(reply[2..])))
            : new InvalidDataException($"Unexpected Game Boy worker reply: {reply}");

    public async ValueTask DisposeAsync()
    {
        foreach (Worker worker in this.workers) worker.Stop();
        await Task.WhenAll(this.workers.Select(worker => worker.WaitForExitAsync()));
        foreach (Worker worker in this.workers) worker.Dispose();
    }

    private sealed class Worker : IDisposable
    {
        private readonly Process process;
        private readonly SemaphoreSlim gate = new(1, 1);
        private long assignmentCount;

        public Worker(string? romPath)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add(typeof(GameboyWorkerPool).Assembly.Location);
            start.ArgumentList.Add("--worker");
            if (romPath is not null)
            {
                start.ArgumentList.Add("--rom");
                start.ArgumentList.Add(romPath);
            }
            this.process = Process.Start(start) ?? throw new InvalidOperationException("Could not start a Game Boy worker.");
            this.process.StandardInput.AutoFlush = true;
        }

        public long AssignmentCount => Interlocked.Read(ref this.assignmentCount);
        public long WorkingSetBytes
        {
            get
            {
                this.process.Refresh();
                return this.process.WorkingSet64;
            }
        }

        public async Task<string> ExchangeAsync(string request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.assignmentCount);
            await this.gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await this.process.StandardInput.WriteLineAsync(request);
                // Once sent, consume this reply before allowing another request on
                // the pipe, even if the HTTP request has been cancelled.
                string? reply = await this.process.StandardOutput.ReadLineAsync();
                return reply ?? throw new EndOfStreamException("Game Boy worker closed its output pipe.");
            }
            finally
            {
                this.gate.Release();
            }
        }

        public void Stop()
        {
            if (this.process.HasExited) return;
            this.process.StandardInput.Close();
        }

        public async Task WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await this.process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!this.process.HasExited) this.process.Kill(entireProcessTree: true);
                await this.process.WaitForExitAsync();
            }
        }

        public void Dispose()
        {
            this.gate.Dispose();
            this.process.Dispose();
        }
    }
}
