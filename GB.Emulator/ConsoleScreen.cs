#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using GB.Emulator.Core;
using GB.Emulator.Display;

namespace GB.Emulator
{
    /// <summary>Presents hardware frames in the terminal and can repaint on demand.</summary>
    internal sealed class ConsoleScreen : IDisposable
    {
        private readonly Gameboy gameboy;
        private readonly byte[] shades = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly long frameDuration = Stopwatch.Frequency / 60;
        private long nextFrame;
        private int lastWidth;
        private int lastHeight;
        private bool hasFrame;

        public ConsoleScreen(Gameboy gameboy)
        {
            this.gameboy = gameboy;
            this.gameboy.Video.FrameReady += this.OnFrameReady;
        }

        public void Redraw(bool clear = false)
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            if (width < ConsoleVideoRenderer.Columns || height < ConsoleVideoRenderer.Rows + 1)
                return;

            if (clear || width != this.lastWidth || height != this.lastHeight)
                Console.Clear();
            this.lastWidth = width;
            this.lastHeight = height;

            DmgScreenRenderer.RenderScreen(this.gameboy.CaptureVideoState(), this.shades);
            ConsoleVideoRenderer.Draw(this.shades);
            Console.SetCursorPosition(0, ConsoleVideoRenderer.Rows);
            string status = this.hasFrame
                ? "Arrows move · Z A · X B · Enter Start · Space Select · R redraw · Ctrl+C stop"
                : "Loading screen... Arrows/Z/X/Enter/Space · R redraw · Ctrl+C stop";
            Console.Write(status.PadRight(ConsoleVideoRenderer.Columns - 1));
        }

        public void Dispose() => this.gameboy.Video.FrameReady -= this.OnFrameReady;

        private void OnFrameReady(object? sender, EventArgs e)
        {
            this.hasFrame = true;
            this.Redraw();

            this.nextFrame += this.frameDuration;
            long remaining = this.nextFrame - this.clock.ElapsedTicks;
            if (remaining > 0)
                Thread.Sleep(TimeSpan.FromSeconds((double)remaining / Stopwatch.Frequency));
            else
                this.nextFrame = this.clock.ElapsedTicks;
        }
    }
}
