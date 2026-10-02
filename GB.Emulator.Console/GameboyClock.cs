using System;
using System.Diagnostics;
using System.Threading;

namespace GB.Emulator;

/// <summary>Polls input regularly and keeps emulated cycles near wall time.</summary>
internal sealed class GameboyClock
{
    private const double CyclesPerSecond = 4194304.0;
    private readonly long started = Stopwatch.GetTimestamp();
    private int steps;

    public bool InputPollDue() => (++this.steps & 0xFF) == 0;

    public void Pace(long emulatedCycles, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested &&
               emulatedCycles / CyclesPerSecond - Stopwatch.GetElapsedTime(this.started).TotalSeconds > 0.002)
            Thread.Sleep(1);
    }

    public void WaitForButton() => Thread.Sleep(10);
}
