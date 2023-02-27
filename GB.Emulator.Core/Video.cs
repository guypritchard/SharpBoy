using GB.Emulator.Core.InputOutput;
using System;
using System.Drawing;

namespace GB.Emulator.Core
{
    public class Video
    {
        public Video(Lcd lcd)
        {
            this.lcd = lcd;
        }

        private const int Width = 160;
        private const int Height = 144;
        private readonly Lcd lcd;
        private byte[] memory = new byte[Video.Width * Video.Height];
        private Bitmap frame = new Bitmap(Video.Width, Video.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

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
