#nullable enable
using System;
using System.Diagnostics;
using GB.Emulator.Core;
using GB.Emulator.Core.Rendering;
using GB.Emulator.Hosting;

namespace GB.Emulator
{
    /// <summary>Presents hardware frames in the terminal and can repaint on demand.</summary>
    internal sealed class ConsoleScreen : IVideoOutput
    {
        private readonly EmulationHost host;
        private VideoFrame? frame;
        private readonly byte[] shades = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
        private readonly byte[] previousShades = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
        private bool hasDrawnFrame;
        private string? lastStatus;
        private int lastWidth;
        private int lastHeight;

        public ConsoleScreen(EmulationHost host)
        {
            this.host = host;
        }

        public void Present(VideoFrame frame) { this.frame = frame; this.Redraw(); }

        public void Redraw(bool clear = false)
        {
            if (this.frame == null) return;
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

            this.frame.Pixels.CopyTo(this.shades);
            if (fullRedraw) ConsoleVideoRenderer.Draw(this.shades);
            else ConsoleVideoRenderer.DrawChanges(this.shades, this.previousShades);
            this.shades.CopyTo(this.previousShades, 0);
            this.hasDrawnFrame = true;
            this.DrawStatus();
        }

        private void DrawStatus()
        {
            string controls = this.frame != null
                ? "Arrows move · Z A · X B · Enter Start · Space Select · R redraw · Ctrl+C stop"
                : "Loading screen... Arrows/Z/X/Enter/Space · R redraw · Ctrl+C stop";
            string status = $"FPS: {this.host.Statistics.FramesPerSecond:F1}  {controls}";
            if (status == this.lastStatus) return;
            Console.SetCursorPosition(0, ConsoleVideoRenderer.Rows);
            Console.Write(status.PadRight(ConsoleVideoRenderer.Columns - 1));
            this.lastStatus = status;
        }

    }
}
