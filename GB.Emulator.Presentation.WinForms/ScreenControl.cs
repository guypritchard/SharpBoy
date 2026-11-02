using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GB.Emulator.Core;
using GB.Emulator.Core.Rendering;
using GB.Emulator.Hosting;

namespace GB.Emulator.Presentation.WinForms;

public sealed class ScreenControl : Control, IVideoOutput
{
    private readonly Bitmap bitmap = new(Video.Width, Video.Height, PixelFormat.Format32bppArgb);
    private readonly int[] pixels = new int[Video.Width * Video.Height];
    private static readonly int[] Palette =
    [unchecked((int)0xFFE0F8D0), unchecked((int)0xFF88C070), unchecked((int)0xFF346856), unchecked((int)0xFF081820)];
    private bool hasFrame;
    private bool integerScaling = true;

    public ScreenControl()
    {
        this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        this.BackColor = Color.FromArgb(18, 24, 29);
        this.ForeColor = Color.FromArgb(208, 218, 224);
        this.TabStop = false;
    }

    [System.ComponentModel.DefaultValue(true)]
    public bool IntegerScaling
    {
        get => this.integerScaling;
        set { this.integerScaling = value; this.Invalidate(); }
    }

    public void Present(VideoFrame frame)
    {
        for (int i = 0; i < this.pixels.Length; i++) this.pixels[i] = Palette[frame.Pixels[i]];
        BitmapData data = this.bitmap.LockBits(new Rectangle(0, 0, Video.Width, Video.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(this.pixels, 0, data.Scan0, this.pixels.Length); }
        finally { this.bitmap.UnlockBits(data); }
        this.hasFrame = true;
        this.Invalidate();
    }

    internal static Rectangle GetViewport(Size available, bool integerScaling)
    {
        double scale = Math.Min(available.Width / (double)Video.Width, available.Height / (double)Video.Height);
        if (scale <= 0) return Rectangle.Empty;
        if (integerScaling && scale >= 1) scale = Math.Floor(scale);
        int width = (int)(Video.Width * scale);
        int height = (int)(Video.Height * scale);
        return new Rectangle((available.Width - width) / 2, (available.Height - height) / 2, width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!this.hasFrame)
        {
            TextRenderer.DrawText(e.Graphics, string.IsNullOrEmpty(this.Text) ? "SHARPBOY" : this.Text,
                this.Font, this.ClientRectangle, this.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        Rectangle target = GetViewport(this.ClientSize, this.IntegerScaling);
        if (target.IsEmpty) return;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(this.bitmap, target, 0, 0, Video.Width, Video.Height, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.bitmap.Dispose();
        base.Dispose(disposing);
    }
}
