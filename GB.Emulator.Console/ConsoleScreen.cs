#nullable enable
using System;
using System.Diagnostics;
using GB.Emulator.Core;
using GB.Emulator.Display;

namespace GB.Emulator
{
    /// <summary>Presents hardware frames in the terminal and can repaint on demand.</summary>
    internal sealed class ConsoleScreen : IDisposable
    {
        private readonly Gameboy gameboy;
        private readonly byte[] shades = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
        private readonly byte[] previousShades = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long fpsWindowStart;
        private int framesInWindow;
        private double fps;
        private bool fpsAvailable;
        private bool hasDrawnFrame;
        private string? lastStatus;
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

            bool fullRedraw = clear || width != this.lastWidth || height != this.lastHeight || !this.hasDrawnFrame;
            if (fullRedraw)
            {
                Console.Clear();
                this.lastStatus = null;
            }
            this.lastWidth = width;
            this.lastHeight = height;

            DmgScreenRenderer.RenderScreen(this.gameboy.CaptureVideoState(), this.shades);
            if (fullRedraw) ConsoleVideoRenderer.Draw(this.shades);
            else ConsoleVideoRenderer.DrawChanges(this.shades, this.previousShades);
            this.shades.CopyTo(this.previousShades, 0);
            this.hasDrawnFrame = true;
            this.DrawStatus();
        }

        private void DrawStatus()
        {
            string controls = this.hasFrame
                ? "Arrows move · Z A · X B · Enter Start · Space Select · R redraw · Ctrl+C stop"
                : "Loading screen... Arrows/Z/X/Enter/Space · R redraw · Ctrl+C stop";
            string status = $"FPS: {(this.fpsAvailable ? this.fps.ToString("F1") : "--")}  {controls}";
            if (status == this.lastStatus) return;
            Console.SetCursorPosition(0, ConsoleVideoRenderer.Rows);
            Console.Write(status.PadRight(ConsoleVideoRenderer.Columns - 1));
            this.lastStatus = status;
        }

        public void Dispose() => this.gameboy.Video.FrameReady -= this.OnFrameReady;

        private void OnFrameReady(object? sender, EventArgs e)
        {
            this.hasFrame = true;
            this.Redraw();
            this.framesInWindow++;
            long now = this.clock.ElapsedTicks;
            if (this.fpsWindowStart == 0) this.fpsWindowStart = now;
            long elapsed = now - this.fpsWindowStart;
            if (elapsed < Stopwatch.Frequency) return;
            this.fps = this.framesInWindow * (double)Stopwatch.Frequency / elapsed;
            this.fpsAvailable = true;
            this.framesInWindow = 0;
            this.fpsWindowStart = now;
            this.DrawStatus();
        }
    }
}
