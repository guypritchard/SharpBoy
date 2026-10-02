using System.Drawing.Drawing2D;

namespace GB.Debugger;

internal sealed class FrameDisplayControl : Control
{
    private Bitmap? frame;

    public FrameDisplayControl()
    {
        this.DoubleBuffered = true;
        this.ResizeRedraw = true;
        this.BackColor = Color.FromArgb(226, 232, 240);
    }

    public void SetFrame(Bitmap? nextFrame)
    {
        Bitmap? previous = this.frame;
        this.frame = nextFrame;
        previous?.Dispose();
        this.Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (this.frame == null)
        {
            TextRenderer.DrawText(e.Graphics, "Load a ROM to see the screen preview.",
                this.Font, this.ClientRectangle, SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        float scale = Math.Min((float)this.ClientSize.Width / this.frame.Width,
            (float)this.ClientSize.Height / this.frame.Height);
        if (scale <= 0) return;
        // Prefer whole-pixel scaling when the panel has room for it.
        if (scale >= 1) scale = Math.Max(1, (int)scale);
        int width = (int)(this.frame.Width * scale);
        int height = (int)(this.frame.Height * scale);
        int x = (this.ClientSize.Width - width) / 2;
        int y = (this.ClientSize.Height - height) / 2;

        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(this.frame, new Rectangle(x, y, width, height),
            0, 0, this.frame.Width, this.frame.Height, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.frame?.Dispose();
            this.frame = null;
        }
        base.Dispose(disposing);
    }
}
