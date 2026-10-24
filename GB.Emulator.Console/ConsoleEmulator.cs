#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using GB.Emulator.Core;
using GB.Emulator.Core.Win32;
using GB.Emulator.Hosting;

namespace GB.Emulator;

/// <summary>Connects the Game Boy hardware to terminal video, audio, and input.</summary>
internal sealed class ConsoleEmulator : IDisposable
{
    private readonly EmulationHost host;
    private readonly ConsoleScreen screen;
    private readonly ConsoleKeyboardInput keyboard;
    private bool disposed;

    private ConsoleEmulator(EmulationHost host)
    {
        this.host = host;
        this.screen = new ConsoleScreen(host);
        this.keyboard = new ConsoleKeyboardInput(host.Input);
    }

    public static async Task<ConsoleEmulator> LoadAsync(string[] args)
    {
        string romPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(AppContext.BaseDirectory, "SPRITE.GB");
        var host = new EmulationHost(audio: WindowsAudioOutput.TryCreate());
        try
        {
            await host.LoadAsync(romPath);
            host.Resume();
            return new ConsoleEmulator(host);
        }
        catch { host.Dispose(); throw; }
    }

    public void Run(CancellationToken stopping)
    {
        var clock = Stopwatch.StartNew();
        long previous = clock.ElapsedTicks;
        VideoFrame? presented = null;
        while (!stopping.IsCancellationRequested)
        {
            this.PollInput();
            long now = clock.ElapsedTicks;
            this.host.Advance(TimeSpan.FromSeconds((now - previous) / (double)Stopwatch.Frequency));
            previous = now;
            if (this.host.LatestFrame is VideoFrame frame && !ReferenceEquals(frame, presented))
            {
                this.screen.Present(frame);
                presented = frame;
            }
            Thread.Sleep(1);
        }
    }

    private void PollInput()
    {
        if (this.keyboard.Poll()) this.screen.Redraw(clear: true);
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.keyboard.Dispose();
        this.host.Dispose();
    }
}
