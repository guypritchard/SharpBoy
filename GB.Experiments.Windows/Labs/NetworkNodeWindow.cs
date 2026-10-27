using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GB.Experiments.NetworkCartridge;
using Border = System.Windows.Controls.Border;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;

namespace GB.Experiments.Windows.Labs;

/// <summary>Presentation of detached node telemetry; never accesses a running Gameboy.</summary>
internal sealed class NetworkNodeWindow : Window
{
    private readonly TextBlock state, job, cpu, memory, network;
    internal NativeNodeSnapshot? Snapshot { get; private set; }
    internal BitmapSource Screen { get; }

    internal NetworkNodeWindow(int index, BitmapSource screen)
    {
        Screen = screen;
        Title = $"SharpBoy · GB {index + 1:00} inspector";
        Width = 1080; Height = 770; MinWidth = 760; MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#09121F"); Foreground = Brush("#F0F5FA");
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        var root = new Grid { Margin = new Thickness(24), Background = Background };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        heading.Children.Add(Text($"GB {index + 1:00} / MACHINE INSPECTOR", 24, "#F0F5FA", true));
        state = Text("Waiting for machine telemetry…", 13, "#6FE6CA"); heading.Children.Add(state);
        root.Children.Add(heading);
        var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1.15, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 310 });
        var left = new Grid { Margin = new Thickness(0, 0, 24, 0) };
        left.RowDefinitions.Add(new()); left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var lcd = new Image { Source = screen, Stretch = Stretch.Uniform, Margin = new Thickness(16) };
        RenderOptions.SetBitmapScalingMode(lcd, BitmapScalingMode.NearestNeighbor);
        left.Children.Add(new Border { Child = lcd, Background = Brush("#111E2E"), CornerRadius = new CornerRadius(12) });
        job = Text("", 14, "#A0B2C6"); job.Margin = new Thickness(4, 18, 0, 0); Grid.SetRow(job, 1); left.Children.Add(job);
        body.Children.Add(left);
        var info = new StackPanel();
        TextBlock Section(string label)
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(Text(label, 11, "#6FE6CA", true));
            var value = Text("Waiting…", 13, "#D3DFEB"); value.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(value);
            info.Children.Add(new Border { Child = panel, Background = Brush("#111E2E"), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 12) });
            return value;
        }
        cpu = Section("SM83 / PROCESSOR"); memory = Section("MEMORY / CARTRIDGE"); network = Section("NETWORK / SOCKET OFFLOAD");
        var scroll = new ScrollViewer { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll); Content = root;
        job.Text = "160 x 144 native LCD";
    }

    internal void UpdateFractal(NativeNodeSnapshot? snapshot, int rows, int work, int index, FractalSettings settings)
    {
        job.Text = $"TILE {index % settings.Columns + 1}:{index / settings.Columns + 1}   ·   {rows}/24 rows received\n" +
            $"{rows * 32:N0} / 768 pixels   ·   {work:N0} escape iterations\n160 × 144 native LCD · {settings.Iterations}-iteration limit";
        UpdateMachine(snapshot);
    }
    private void UpdateMachine(NativeNodeSnapshot? snapshot)
    {
        Snapshot = snapshot;
        if (snapshot == null)
        {
            state.Text = "Waiting for machine telemetry…";
            cpu.Text = memory.Text = network.Text = "Waiting…"; return;
        }
        var s = snapshot; var r = s.Cpu;
        state.Text = $"{s.State.ToUpperInvariant()}   ·   {s.WallSeconds:F1}s wall time   ·   " +
            (s.State == "Running" ? "Sampled live inspection" : "Final inspection retained");
        cpu.Text = $"DMG clock: 4.194304 MHz\nMeasured: {s.CyclesPerSecond / 1_000_000:F2} Mcycles/s ({s.Speed:F2}×)\n" +
            $"{s.Cycles:N0} emulated clock cycles\n" +
            $"PC {r.PC:X4}   SP {r.SP:X4}\nA {r.A:X2}  F {r.F:X2}   BC {r.BC:X4}\nDE {r.DE:X4}   HL {r.HL:X4}\n" +
            $"LCD line {s.Hardware.Scanline} · halted {s.Hardware.Halted}\nSpeed includes network waits and scheduling.";
        memory.Text = $"{s.Cartridge.Title.Trim()}\nROM: {s.Cartridge.RomBytes:N0} bytes\n" +
            $"WRAM: 8,192 bytes capacity\nWRAM with non-zero data: {s.NonZeroWorkRamBytes:N0} bytes\n" +
            $"VRAM: 8,192 bytes · HRAM: 127 bytes\nCartridge RAM: {s.Cartridge.DeclaredRamBytes.GetValueOrDefault():N0} bytes\n" +
            "Static guest storage · no managed heap.\nNon-zero bytes do not measure allocation.";
        network.Text = $"{s.Transport} · local PC / loopback\nLocal: {s.Tcp.LocalEndpoint ?? "not connected"}\nCollector: {s.Tcp.RemoteEndpoint}\n" +
            $"Payload sent: {s.Tcp.SentBytes:N0} bytes\nPayload received: {s.Tcp.ReceivedBytes:N0} bytes\n" +
            $"Socket: {((s.NetworkStatus & NetworkCartridgeDevice.Connected) != 0 ? "connected" : "closed")} · error: {s.NetworkError}\n" +
            $"Cartridge buffers: {s.NetworkBufferBytes:N0} bytes each\nEndpoints retained after socket closes.";
    }
    internal void UpdateLife(NativeNodeSnapshot? snapshot, LifeSettings settings, int generation, int index)
    {
        UpdateMachine(snapshot);
        job.Text = $"TERRITORY {index % settings.Columns + 1}:{index / settings.Columns + 1}       32 x 24 cells\n" +
            $"World generation {generation} / {settings.Generations} committed\nConway B3/S23   toroidal boundaries   TCP halo exchange";
    }

    internal void Capture(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)((FrameworkElement)Content).ActualWidth, (int)((FrameworkElement)Content).ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush((Visual)Content), null, new System.Windows.Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path); encoder.Save(stream);
    }
    private static SolidColorBrush Brush(string color) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    private static TextBlock Text(string value, double size, string color, bool bold = false) => new()
    {
        Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
}
