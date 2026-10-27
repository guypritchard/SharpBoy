using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Pen = System.Windows.Media.Pen;
using FlowDirection = System.Windows.FlowDirection;

namespace GB.Emulator.Windows.Desktop;

/// <summary>Local ROM identity and optional label scan. ROM bytes never imply unavailable printed artwork.</summary>
internal sealed record CartridgeAppearance(string Title, string Sha256, int Bytes, Color Shell, BitmapSource? Label, string? LabelPath)
{
    public static CartridgeAppearance Read(string path, string title)
    {
        byte[] rom = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(rom)).ToLowerInvariant();
        string filename = Path.GetFileNameWithoutExtension(path);
        string identity = (title + " " + filename).ToUpperInvariant();
        var shell = Color.FromRgb(177, 175, 166);
        if (identity.Contains("POKEMON") || identity.Contains("POKÉMON"))
        {
            if (identity.Contains("RED")) shell = Color.FromRgb(166, 39, 42);
            else if (identity.Contains("BLUE")) shell = Color.FromRgb(38, 67, 122);
            else if (identity.Contains("YELLOW")) shell = Color.FromRgb(221, 174, 25);
            else if (identity.Contains("GOLD")) shell = Color.FromRgb(156, 130, 65);
            else if (identity.Contains("SILVER")) shell = Color.FromRgb(156, 161, 166);
        }
        // Colour-capable games commonly use the dark dual-mode cartridge; CGB-only
        // games are rejected by the DMG host rather than presented as playable.
        else if (rom.Length > 0x143 && rom[0x143] == 0x80) shell = Color.FromRgb(50, 52, 55);
        string[] candidates = [Path.ChangeExtension(path, ".label.png"), Path.ChangeExtension(path, ".label.jpg"),
            Path.Combine(Path.GetDirectoryName(path)!, "CartridgeLabels", hash + ".png")];
        foreach (string candidate in candidates.Where(File.Exists))
        {
            try
            {
                if (new FileInfo(candidate).Length > 16_000_000) continue;
                using var stream = File.OpenRead(candidate);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1024; image.StreamSource = stream; image.EndInit(); image.Freeze();
                return new(filename, hash, rom.Length, shell, image, candidate);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException)
            { /* An unreadable optional label must not prevent playing a valid ROM. */ }
        }
        return new(filename, hash, rom.Length, shell, null, null);
    }

    public BitmapSource CreateTexture(BitmapSource original)
    {
        var source = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        int stride = source.PixelWidth * 4;
        byte[] pixels = new byte[stride * source.PixelHeight]; source.CopyPixels(pixels, stride, 0);
        // Retain the artist's wear, moulding and PCB details while recolouring the blue plastic.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i], g = pixels[i+1], r = pixels[i+2];
            if (b > r * 1.2 && b > g * 1.07)
            {
                double light = Math.Clamp((r * .21 + g * .72 + b * .07) / 53, .12, 1.12);
                pixels[i] = (byte)Math.Min(255, Shell.B * light);
                pixels[i+1] = (byte)Math.Min(255, Shell.G * light);
                pixels[i+2] = (byte)Math.Min(255, Shell.R * light);
            }
        }
        var recoloured = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(recoloured, new Rect(0, 0, 1024, 1024));
            var label = new Rect(7, 541, 327, 323);
            drawing.DrawRoundedRectangle(new SolidColorBrush(Shell), null, new Rect(2, 532, 340, 340), 8, 8);
            drawing.PushClip(new RectangleGeometry(label, 9, 9));
            // The authored rear label UV island reverses its vertical axis.
            drawing.PushTransform(new ScaleTransform(1, -1, label.X + label.Width/2, label.Y + label.Height/2));
            if (Label != null) drawing.DrawImage(Label, label);
            else
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(228, 218, 189)), null, label);
                drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(59, 65, 111), Color.FromRgb(27, 31, 60), 30), null,
                    new Rect(label.X, label.Y, label.Width, 42));
                Text(drawing, "GAME BOY", 24, Brushes.White, label.X + 19, label.Y + 5, 290);
                string name = Title.Replace(" (World)", "").Replace(" (USA, Europe)", "").Replace(" (1)", "");
                Text(drawing, name.ToUpperInvariant(), 21, new SolidColorBrush(Color.FromRgb(30, 36, 38)), label.X + 20, label.Y + 50, 286);
                drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(160, 142, 102)), 2),
                    new Point(label.X+20, label.Bottom-75), new Point(label.Right-20, label.Bottom-75));
                Text(drawing, "SHARPBOY  ·  " + Bytes / 1024 + " KiB", 14, Brushes.DimGray, label.X+20, label.Bottom-57, 290);
                Text(drawing, "ROM " + Sha256[..12].ToUpperInvariant(), 13, Brushes.DimGray, label.X+20, label.Bottom-34, 290);
            }
            drawing.Pop(); drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(1024, 1024, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static void Text(DrawingContext drawing, string text, double size, Brush brush, double x, double y, double width)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush, 1) { MaxTextWidth = width, MaxTextHeight = 130, Trimming = TextTrimming.CharacterEllipsis };
        drawing.DrawText(formatted, new Point(x, y));
    }
}
