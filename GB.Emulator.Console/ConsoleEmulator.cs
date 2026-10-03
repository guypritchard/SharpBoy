#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GB.Emulator.Core;
using GB.Emulator.Audio;

namespace GB.Emulator;

/// <summary>Connects the Game Boy hardware to terminal video, audio, and input.</summary>
internal sealed class ConsoleEmulator : IDisposable
{
    private readonly Gameboy gameboy;
    private readonly ConsoleScreen screen;
    private readonly ConsoleKeyboardInput keyboard;
    private readonly WindowsAudioOutput? audio;

    private ConsoleEmulator(Gameboy gameboy)
    {
        this.gameboy = gameboy;
        this.screen = new ConsoleScreen(gameboy);
        this.keyboard = new ConsoleKeyboardInput(gameboy.Input);
        this.audio = WindowsAudioOutput.TryCreate(gameboy.Sound);
    }

    public static async Task<ConsoleEmulator> LoadAsync(string[] args)
    {
        string romPath = args.Length > 0
            ? Path.GetFullPath(args[0])
            : DefaultRomPath();
        var gameboy = new Gameboy();
        gameboy.Load(await CartridgeLoader.Load(romPath));
        return new ConsoleEmulator(gameboy);
    }

    private static string DefaultRomPath()
    {
        string tetris = Path.Combine(AppContext.BaseDirectory, "Tetris (World).gb");
        return File.Exists(tetris)
            ? tetris
            : Path.Combine(AppContext.BaseDirectory, "SPRITE.GB");
    }

    public void Run(CancellationToken stopping)
    {
        var clock = new GameboyClock();
        this.screen.Redraw(clear: true);
        while (!stopping.IsCancellationRequested)
        {
            if (this.gameboy.RunStep())
            {
                this.PollInput();
                clock.WaitForButton();
            }
            else if (clock.InputPollDue())
            {
                this.PollInput();
                clock.Pace(this.gameboy.EmulatedCycles, stopping);
            }
        }
    }

    private void PollInput()
    {
        if (this.keyboard.Poll()) this.screen.Redraw(clear: true);
    }

    public void Dispose()
    {
        this.keyboard.Dispose();
        this.screen.Dispose();
        this.audio?.Dispose();
    }
}
