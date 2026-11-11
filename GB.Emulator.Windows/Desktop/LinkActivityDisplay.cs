using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace GB.Emulator.Windows.Desktop;

internal readonly record struct LinkActivitySnapshot(bool Online, bool Connected, long ReadBytes, long WrittenBytes, double Seconds, Guid Source = default);
internal readonly record struct LinkActivitySample(double ReadPerSecond, double WrittenPerSecond, bool Online, bool Connected);

/// <summary>A native-size four-shade LCD. History contains measured serial payload rates,
/// including idle/disconnected intervals, rather than decorative activity animations.</summary>
internal sealed class LinkActivityDisplay
{
    internal const int Width = 160, Height = 144, HistoryLength = 144;
    internal const double SampleSeconds = .25;
    private readonly byte[] pixels = new byte[Width * Height];
    private readonly LinkActivitySample[] history = new LinkActivitySample[HistoryLength];
    private readonly WriteableBitmap bitmap = new(Width, Height, 96, 96, PixelFormats.Indexed8,
        new BitmapPalette(new[] { Color.FromRgb(195,207,148), Color.FromRgb(143,161,92), Color.FromRgb(76,103,57), Color.FromRgb(29,50,31) }));
    private int next, count;
    private bool initialized, online, connected;
    private long lastRead, lastWritten;
    private double pendingRead, pendingWritten, nextSample, lastTime;
    private Guid source;
    private bool dirty;
    internal BitmapSource Image => bitmap;
    internal IReadOnlyList<LinkActivitySample> History => Enumerable.Range(0, count).Select(i => history[(next - count + i + HistoryLength) % HistoryLength]).ToArray();
    internal string ConnectionText => connected ? "CONNECTED" : online ? "WAITING" : "OFFLINE";

    internal LinkActivityDisplay() => Render();

    internal void Update(LinkActivitySnapshot value)
    {
        if (!double.IsFinite(value.Seconds) || value.Seconds < 0 || value.ReadBytes < 0 || value.WrittenBytes < 0) return;
        bool changed = online != value.Online || connected != (value.Online && value.Connected);
        online = value.Online; connected = value.Online && value.Connected;
        if (!initialized || value.Seconds < lastTime)
        {
            initialized = true; lastTime = value.Seconds; nextSample = value.Seconds + SampleSeconds;
            lastRead = value.ReadBytes; lastWritten = value.WrittenBytes; source = value.Source;
            pendingRead = pendingWritten = 0; Render(); return;
        }
        if (source != value.Source || value.ReadBytes < lastRead || value.WrittenBytes < lastWritten)
        {
            // Reconnecting must not turn the new client's lifetime counters into a traffic spike.
            lastRead = value.ReadBytes; lastWritten = value.WrittenBytes; source = value.Source;
            pendingRead = pendingWritten = 0;
        }
        pendingRead += value.ReadBytes - lastRead; pendingWritten += value.WrittenBytes - lastWritten;
        lastRead = value.ReadBytes; lastWritten = value.WrittenBytes; lastTime = value.Seconds;
        if (value.Seconds >= nextSample)
        {
            double due = Math.Floor((value.Seconds - nextSample) / SampleSeconds) + 1;
            double duration = due * SampleSeconds;
            var sample = new LinkActivitySample(pendingRead / duration, pendingWritten / duration, online, connected);
            for (int i = 0; i < Math.Min(HistoryLength, due); i++)
            { history[next] = sample; next = (next + 1) % HistoryLength; count = Math.Min(count + 1, HistoryLength); }
            nextSample += duration; pendingRead = pendingWritten = 0; changed = true;
        }
        if (changed || dirty) Render();
    }

    private void Render()
    {
        Array.Clear(pixels);
        Text(5, 4, "LINK MONITOR", 3);
        Fill(5, 16, 5, 5, connected ? (byte)3 : online ? (byte)2 : (byte)1);
        Text(14, 15, ConnectionText, 3);
        var latest = count == 0 ? default : history[(next + HistoryLength - 1) % HistoryLength];
        Text(5, 29, "READ", 3); TextRight(155, 29, Rate(latest.ReadPerSecond), 3);
        Text(5, 73, "WRITE", 2); TextRight(155, 73, Rate(latest.WrittenPerSecond), 2);
        double peak = Math.Max(1, history.Max(s => Math.Max(s.ReadPerSecond, s.WrittenPerSecond)));
        double scale = Math.Pow(2, Math.Ceiling(Math.Log2(peak)));
        Graph(39, read: true, scale); Graph(83, read: false, scale);
        Text(5, 117, "CABLE", 3); TextRight(155, 117, "36 SEC", 2);
        Fill(5, 127, 150, 7, 1);
        for (int i = 0; i < count; i++)
        {
            var sample = history[(next - count + i + HistoryLength) % HistoryLength];
            Fill(8 + HistoryLength - count + i, 128, 1, 5, sample.Connected ? (byte)3 : sample.Online ? (byte)2 : (byte)0);
        }
        Text(8, 136, "-36S", 2); TextRight(151, 136, "NOW", 2);
        if (!bitmap.TryLock(new Duration(TimeSpan.Zero))) { dirty = true; return; }
        try
        {
            if (bitmap.BackBufferStride == Width) Marshal.Copy(pixels, 0, bitmap.BackBuffer, pixels.Length);
            else for (int y = 0; y < Height; y++) Marshal.Copy(pixels, y * Width, IntPtr.Add(bitmap.BackBuffer, y * bitmap.BackBufferStride), Width);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height)); dirty = false;
        }
        finally { bitmap.Unlock(); }
    }
    private void Graph(int top, bool read, double scale)
    {
        Fill(5, top, 150, 27, 1); Fill(6, top + 1, 148, 25, 0);
        for (int x = 8; x < 152; x += 12)
            for (int y = top + 5; y < top + 25; y += 5) Pixel(x, y, 1);
        int? previousY = null;
        for (int i = 0; i < count; i++)
        {
            var sample = history[(next - count + i + HistoryLength) % HistoryLength];
            double rate = read ? sample.ReadPerSecond : sample.WrittenPerSecond;
            int x = 8 + HistoryLength - count + i, y = top + 24 - (int)Math.Round(Math.Clamp(rate / scale, 0, 1) * 21);
            Fill(x, y + 1, 1, top + 25 - y - 1, 1);
            Fill(x, Math.Min(y, previousY ?? y), 1, Math.Abs(y - (previousY ?? y)) + 1, read ? (byte)3 : (byte)2);
            previousY = y;
        }
    }
    private static string Rate(double value) => value >= 1_000_000 ? (value / 1_000_000).ToString("0.#", CultureInfo.InvariantCulture) + "M B/S" :
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "K B/S" : value.ToString("0", CultureInfo.InvariantCulture) + " B/S";
    private void Pixel(int x, int y, byte shade) { if ((uint)x < Width && (uint)y < Height) pixels[y * Width + x] = shade; }
    private void Fill(int x, int y, int width, int height, byte shade)
    { for (int yy = Math.Max(0, y); yy < Math.Min(Height, y + height); yy++) for (int xx = Math.Max(0, x); xx < Math.Min(Width, x + width); xx++) pixels[yy * Width + xx] = shade; }
    private void TextRight(int right, int y, string text, byte shade) => Text(right - text.Length * 6 + 1, y, text, shade);
    private void Text(int x, int y, string text, byte shade)
    {
        foreach (char c in text)
        {
            if (Glyphs.TryGetValue(c, out string? rows))
                for (int yy = 0; yy < 7; yy++) for (int xx = 0; xx < 5; xx++) if (rows[yy * 6 + xx] == '1') Pixel(x + xx, y + yy, shade);
            x += 6;
        }
    }
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A']="01110/10001/10001/11111/10001/10001/10001", ['B']="11110/10001/10001/11110/10001/10001/11110",
        ['C']="01111/10000/10000/10000/10000/10000/01111", ['D']="11110/10001/10001/10001/10001/10001/11110",
        ['E']="11111/10000/10000/11110/10000/10000/11111", ['F']="11111/10000/10000/11110/10000/10000/10000",
        ['G']="01111/10000/10000/10111/10001/10001/01111", ['H']="10001/10001/10001/11111/10001/10001/10001",
        ['I']="11111/00100/00100/00100/00100/00100/11111", ['K']="10001/10010/10100/11000/10100/10010/10001",
        ['L']="10000/10000/10000/10000/10000/10000/11111", ['M']="10001/11011/10101/10101/10001/10001/10001",
        ['N']="10001/11001/10101/10011/10001/10001/10001", ['O']="01110/10001/10001/10001/10001/10001/01110",
        ['R']="11110/10001/10001/11110/10100/10010/10001", ['S']="01111/10000/10000/01110/00001/00001/11110",
        ['T']="11111/00100/00100/00100/00100/00100/00100", ['W']="10001/10001/10001/10101/10101/11011/10001",
        ['0']="01110/10001/10011/10101/11001/10001/01110", ['1']="00100/01100/00100/00100/00100/00100/01110",
        ['2']="01110/10001/00001/00010/00100/01000/11111", ['3']="11110/00001/00001/01110/00001/00001/11110",
        ['4']="00010/00110/01010/10010/11111/00010/00010", ['5']="11111/10000/10000/11110/00001/00001/11110",
        ['6']="01110/10000/10000/11110/10001/10001/01110", ['7']="11111/00001/00010/00100/01000/01000/01000",
        ['8']="01110/10001/10001/01110/10001/10001/01110", ['9']="01110/10001/10001/01111/00001/00001/01110",
        ['-']="00000/00000/00000/11111/00000/00000/00000", ['.']="00000/00000/00000/00000/00000/00110/00110",
        ['/']="00001/00001/00010/00100/01000/10000/10000"
    };
}
