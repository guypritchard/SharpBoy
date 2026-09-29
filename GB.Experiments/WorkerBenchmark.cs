using System.Diagnostics;

namespace GB.Experiments;

/// <summary>A repeatable local throughput check for choosing a worker count.</summary>
internal static class WorkerBenchmark
{
    public static async Task RunAsync(string? romPath)
    {
        const int requests = 16;
        const int index = 1000;
        Console.WriteLine($"{requests} simultaneous requests for prime #{index} (expected 7919)");
        Console.WriteLine("Workers\tSeconds\tRequests/s\tWorker RAM MiB");
        foreach (int count in new[] { 1, 2, 4, 8, 12, 16 }.Where(count => count <= Environment.ProcessorCount))
        {
            await using var pool = new GameboyWorkerPool(count, romPath);
            var timer = Stopwatch.StartNew();
            int[] results = await Task.WhenAll(Enumerable.Range(0, requests)
                .Select(_ => pool.GetPrimeAsync(index, CancellationToken.None)));
            timer.Stop();
            if (results.Any(result => result != 7919))
                throw new InvalidOperationException("A Game Boy worker returned an unexpected prime.");
            Console.WriteLine($"{count}\t{timer.Elapsed.TotalSeconds:F2}\t{requests / timer.Elapsed.TotalSeconds:F2}\t\t{pool.TotalWorkerWorkingSetBytes / 1048576.0:F0}");
        }
    }
}
