using GB.Emulator.Core.InputOutput;
using System;
using System.Drawing;

namespace GB.Emulator.Core
{
    public class Video
    {
        public Video(Lcd lcd)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            {
                throw new PlatformNotSupportedException("Video rendering requires Windows 7 or later.");
            }

            this.lcd = lcd;
            this.frame = new Bitmap(Video.Width, Video.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        }

        private const int Width = 160;
        private const int Height = 144;
        private readonly Lcd lcd;
        private readonly byte[] memory = new byte[Video.Width * Video.Height];
        private readonly Bitmap frame;

        public void Step()
        {
            this.lcd.Step();
        }

        public void BitBlt(byte[] incomingFrame)
        {
            if (incomingFrame.Length > Video.Width * Video.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(incomingFrame));
            }

            Array.Copy(incomingFrame, this.memory, incomingFrame.Length);
        }

        public Bitmap Render()
        {
            // TODO: Create a new copy of the current frame on Render
            return this.frame;
        }
    }
}
