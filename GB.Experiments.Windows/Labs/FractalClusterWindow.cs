using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GB.Emulator.Hosting;
using GB.Experiments.NetworkCartridge;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace GB.Experiments.Windows.Labs;

/// <summary>Live emulator LCDs with a collector driven only by received TCP row records.</summary>
internal sealed class FractalClusterWindow : Window
{
    private static readonly int[] Palette = [unchecked((int)0xFFADB975), unchecked((int)0xFF829454), unchecked((int)0xFF435437), unchecked((int)0xFF1E3026)];
    private readonly FractalLayout layout;
    private readonly FractalSettings settings;
    private readonly TextBox columnsEditor, rowsEditor, iterationsEditor, delayEditor;
    private readonly WriteableBitmap[] screens;
    private readonly int[] framePixels = new int[160 * 144];
    private readonly TextBlock[] nodeStatus;
    private readonly ProgressBar[] bars;
    private readonly int[] completedRows, work;
    private readonly VideoFrame?[] latestFrames;
    private readonly long[] cycles;
    private readonly NativeNodeSnapshot?[] snapshots;
    private readonly Dictionary<int, NetworkNodeWindow> inspectors = new();
    private readonly ConcurrentQueue<FractalRow> received = new();
    private readonly WriteableBitmap collected;
    private readonly int[] pixels;
    private readonly TextBlock status = Text("Preparing native machines…", 13, "#A0B2C6");
    private readonly TextBlock pixelCount = Text("0", 22, "#F0F5FA");
    private readonly TextBlock iterationCount = Text("0", 22, "#F0F5FA");
    private readonly TextBlock elapsed = Text("0.0s", 22, "#F0F5FA");
    private readonly Button restart = ActionButton("Run again"), stop = ActionButton("Stop"), export = ActionButton("Collect values");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch clock = new();
    private CancellationTokenSource? run;
    private bool closed;
    private readonly string? capturePath;
    private readonly bool captureAndClose;
    internal FractalClusterResult? Result { get; private set; }
    internal Task? RunTask { get; private set; }
    internal IReadOnlyList<int> CompletedRows => completedRows;
    internal FractalSettings Configuration => settings;

    public FractalClusterWindow(string[] args)
    {
        int configIndex = Array.IndexOf(args, "--config");
        if (configIndex >= 0 && configIndex + 1 >= args.Length) throw new ArgumentException("--config requires a JSON file.");
        settings = configIndex >= 0 ? FractalSettings.Load(args[configIndex + 1]) : new();
        if (args.Contains("--workers") && (args.Contains("--columns") || args.Contains("--rows")))
            throw new ArgumentException("Use --workers for an automatic grid, or --columns/--rows for an explicit grid.");
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--capture" && i + 1 < args.Length) capturePath = Path.GetFullPath(args[++i]);
            else if (args[i] == "--capture-and-close") captureAndClose = true;
            else if (args[i] == "--config") i++;
            else if (args[i] == "--workers" && i + 1 < args.Length && int.TryParse(args[++i], out int count))
            { var grid = FractalLayout.ForWorkers(count); settings = settings with { Columns = grid.Columns, Rows = grid.Rows }; }
            else if (args[i] == "--columns" && i + 1 < args.Length && int.TryParse(args[++i], out int columns)) settings = settings with { Columns = columns };
            else if (args[i] == "--rows" && i + 1 < args.Length && int.TryParse(args[++i], out int rows)) settings = settings with { Rows = rows };
            else if (args[i] == "--iterations" && i + 1 < args.Length && int.TryParse(args[++i], out int iterations)) settings = settings with { Iterations = iterations };
            else if (args[i] == "--row-delay-ms" && i + 1 < args.Length && int.TryParse(args[++i], out int delay)) settings = settings with { RowDelayMs = delay };
            else throw new ArgumentException("Usage: --cluster [--workers 1..64 | --columns N --rows N] [--iterations 1..64] [--row-delay-ms 0..1000] [--config settings.json] [--capture image.png] [--capture-and-close]");
        }
        settings.Validate(); layout = settings.Layout;
        int workers = layout.Workers;
        screens = new WriteableBitmap[workers]; nodeStatus = new TextBlock[workers]; bars = new ProgressBar[workers];
        completedRows = new int[workers]; work = new int[workers]; cycles = new long[workers]; latestFrames = new VideoFrame?[workers];
        snapshots = new NativeNodeSnapshot?[workers];
        collected = new(layout.Width, layout.Height, 96, 96, PixelFormats.Bgra32, null);
        pixels = new int[layout.Width * layout.Height];
        Title = $"SharpBoy · {workers}-machine parallel lab";
        Width = Math.Min(layout.Columns >= 8 ? 1850 : workers >= 16 ? 1650 : 1500, SystemParameters.WorkArea.Width - 30);
        Height = Math.Min(1000, SystemParameters.WorkArea.Height - 40);
        MinWidth = 1100; MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#09121F"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(8, 0, 8, 16) };
        var badge = Text($"{workers:00} NODES   /   TCP LOCALHOST", 12, "#6FE6CA"); badge.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(badge, Dock.Right); header.Children.Add(badge);
        var heading = new StackPanel(); heading.Children.Add(Text($"{workers} machines. One image.", 30, "#F0F5FA", true));
        heading.Children.Add(Text("Native SM83 Mandelbrot rendering · each Game Boy owns one tile", 13, "#93A7BE"));
        header.Children.Add(heading); root.Children.Add(header);
        var body = new Grid(); Grid.SetRow(body, 1);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(layout.Columns >= 8 ? 3 : 1.9, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
        var nodes = new UniformGrid { Rows = layout.Rows, Columns = layout.Columns, MinWidth = layout.Columns * 140, MinHeight = layout.Rows * 150 };
        for (int i = 0; i < workers; i++)
        {
            screens[i] = new WriteableBitmap(160, 144, 96, 96, PixelFormats.Bgra32, null);
            var panel = new Grid { Margin = new Thickness(layout.Columns >= 8 ? 6 : 12, 8, layout.Columns >= 8 ? 6 : 12, 10) };
            panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new() { Height = new GridLength(4) });
            panel.RowDefinitions.Add(new() { Height = new GridLength(38) });
            var title = Text($"GB {i+1:00}   ·   {i%layout.Columns+1}:{i/layout.Columns+1}", 11, "#7CDEC8", true);
            panel.Children.Add(title);
            var view = new Image { Source = screens[i], Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 10) };
            RenderOptions.SetBitmapScalingMode(view, BitmapScalingMode.NearestNeighbor);
            int nodeIndex = i;
            view.Cursor = System.Windows.Input.Cursors.Hand;
            view.ToolTip = $"Inspect GB {i + 1:00} � larger screen, CPU, memory and network";
            view.Focusable = true;
            view.MouseLeftButtonUp += (_, _) => OpenNodeDetails(nodeIndex);
            view.KeyDown += (_, e) =>
            {
                if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space)
                { OpenNodeDetails(nodeIndex); e.Handled = true; }
            };
            Grid.SetRow(view, 1); panel.Children.Add(view);
            bars[i] = new ProgressBar { Minimum = 0, Maximum = 24, Height = 4, Foreground = Brush("#6FE6CA"), Background = Brush("#263346"), BorderThickness = new Thickness(0) };
            Grid.SetRow(bars[i], 2); panel.Children.Add(bars[i]);
            nodeStatus[i] = Text("WAITING FOR JOB", 10, "#91A6BE"); nodeStatus[i].Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(nodeStatus[i], 3); panel.Children.Add(nodeStatus[i]);
            nodes.Children.Add(new Border { Child = panel, Background = Brush("#111E2E"), CornerRadius = new CornerRadius(12), Margin = new Thickness(5), BorderBrush = Brush("#223248"), BorderThickness = new Thickness(1) });
        }
        var nodeScroll = new ScrollViewer { Content = nodes, Margin = new Thickness(0, 0, 12, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        nodeScroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) return;
            nodes.Width = Math.Max(nodes.MinWidth, nodeScroll.ViewportWidth);
            nodes.Height = Math.Max(nodes.MinHeight, nodeScroll.ViewportHeight);
        };
        body.Children.Add(nodeScroll);
        var collector = new StackPanel { Margin = new Thickness(20) };
        collector.Children.Add(Text("COLLECTOR / 01", 12, "#6FE6CA", true));
        var collectorTitle = Text("The shared picture", 24, "#F0F5FA", true); collectorTitle.Margin = new Thickness(0, 8, 0, 8); collector.Children.Add(collectorTitle);
        var explanation = Text("Each incoming row is verified, then placed into the combined image. The pixels are computed inside the Game Boys.", 12, "#93A7BE");
        explanation.TextWrapping = TextWrapping.Wrap; explanation.Margin = new Thickness(0, 0, 0, 18); collector.Children.Add(explanation);
        var image = new Image { Source = collected, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var picture = new Grid { Width = 324, Height = 324d * layout.Height / layout.Width, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        picture.Children.Add(image);
        var tileLines = new UniformGrid { Rows = layout.Rows, Columns = layout.Columns, IsHitTestVisible = false };
        for (int i = 0; i < workers; i++) tileLines.Children.Add(new Border { BorderThickness = new Thickness(.5), BorderBrush = Brush("#4C7967") });
        picture.Children.Add(tileLines); collector.Children.Add(new Viewbox { Child = picture, StretchDirection = StretchDirection.DownOnly, Stretch = Stretch.Uniform });
        collector.Children.Add(Metric("PIXELS RECEIVED", pixelCount));
        collector.Children.Add(Metric("ESCAPE ITERATIONS", iterationCount));
        collector.Children.Add(Metric("WALL TIME", elapsed));
        var protocol = Text($"Q5 fixed-point · {layout.Width} × {layout.Height} pixels\n{workers} independent CPUs · {settings.Iterations}-iteration limit\nLCDs show native guest video", 12, "#93A7BE"); protocol.Margin = new Thickness(0, 18, 0, 20); collector.Children.Add(protocol);
        collector.Children.Add(Text("CONFIGURE THE NEXT RUN", 11, "#6FE6CA", true));
        var fields = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        TextBox Field(string label, int value)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
            group.Children.Add(Text(label, 10, "#93A7BE"));
            var editor = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = 68, Padding = new Thickness(6),
                Foreground = Brush("#F0F5FA"), Background = Brush("#172B3F"), BorderBrush = Brush("#476079") };
            group.Children.Add(editor); fields.Children.Add(group); return editor;
        }
        columnsEditor = Field("Columns", settings.Columns); rowsEditor = Field("Rows", settings.Rows);
        iterationsEditor = Field("Iterations", settings.Iterations); delayEditor = Field("Row delay ms", settings.RowDelayMs);
        collector.Children.Add(fields);
        var countLabel = Text("", 11, "#93A7BE"); countLabel.Margin = new Thickness(0, 0, 0, 8); collector.Children.Add(countLabel);
        void ShowCount()
        {
            try { countLabel.Text = $"{ReadConfiguration().Layout.Workers} Game Boys · maximum 64"; }
            catch (ArgumentException ex) { countLabel.Text = ex.Message.Split('\n')[0]; }
        }
        foreach (var editor in new[] { columnsEditor, rowsEditor, iterationsEditor, delayEditor }) editor.TextChanged += (_, _) => ShowCount();
        ShowCount();
        var configButtons = new WrapPanel();
        var apply = ActionButton("Apply & run"); var load = ActionButton("Load…"); var save = ActionButton("Save…");
        configButtons.Children.Add(apply); configButtons.Children.Add(load); configButtons.Children.Add(save); collector.Children.Add(configButtons);
        apply.Click += (_, _) =>
        {
            try
            {
                var config = ReadConfiguration();
                var next = new FractalClusterWindow(["--columns", config.Columns.ToString(), "--rows", config.Rows.ToString(),
                    "--iterations", config.Iterations.ToString(), "--row-delay-ms", config.RowDelayMs.ToString()]);
                next.Show(); Close();
            }
            catch (ArgumentException ex) { status.Text = ex.Message.Split('\n')[0]; }
        };
        load.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Lab configuration|*.json", Title = "Load lab configuration" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var config = FractalSettings.Load(dialog.FileName);
                columnsEditor.Text = config.Columns.ToString(); rowsEditor.Text = config.Rows.ToString();
                iterationsEditor.Text = config.Iterations.ToString(); delayEditor.Text = config.RowDelayMs.ToString();
                status.Text = "Settings loaded. Apply & run starts the configured cluster.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException) { status.Text = "Could not load settings: " + ex.Message; }
        };
        save.Click += (_, _) =>
        {
            try
            {
                var config = ReadConfiguration();
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Lab configuration|*.json", FileName = "gameboy-lab.json" };
                if (dialog.ShowDialog(this) == true) { config.Save(dialog.FileName); status.Text = "Settings saved: " + dialog.FileName; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { status.Text = "Could not save settings: " + ex.Message; }
        };
        var buttons = new WrapPanel(); buttons.Children.Add(restart); buttons.Children.Add(stop); buttons.Children.Add(export); collector.Children.Add(buttons);
        var collectHint = Text("Collect values exports JSON, per-pixel CSV,\nper-node CSV and the assembled PNG.", 11, "#91A6BE"); collectHint.Margin = new Thickness(0, 12, 0, 0); collector.Children.Add(collectHint);
        var right = new Border { Child = new ScrollViewer { Content = collector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = Brush("#101D2D"), CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 5, 0, 5) };
        Grid.SetColumn(right, 1); body.Children.Add(right); root.Children.Add(body);
        status.Margin = new Thickness(8, 12, 8, 0); Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        restart.Click += (_, _) => StartRun(); stop.Click += (_, _) => StopRun();
        export.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Collect parallel results", Filter = "JSON results|*.json", FileName = "gameboy-fractal.json" };
            if (dialog.ShowDialog(this) == true)
            {
                try { ExportResults(dialog.FileName); status.Text = "Saved JSON, pixel CSV, node CSV and PNG: " + dialog.FileName; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Could not save results: " + ex.Message; }
            }
        };
        timer.Tick += (_, _) => RefreshPresentation();
        Loaded += (_, _) => { timer.Start(); StartRun(); };
        Closed += (_, _) => { closed = true; timer.Stop(); run?.Cancel(); };
    }

    internal NetworkNodeWindow OpenNodeDetails(int index)
    {
        if ((uint)index >= (uint)screens.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!inspectors.TryGetValue(index, out var inspector))
        {
            inspector = new NetworkNodeWindow(index, screens[index]) { Owner = this };
            inspectors.Add(index, inspector);
            inspector.Closed += (_, _) => inspectors.Remove(index);
            inspector.Show();
        }
        inspector.UpdateFractal(Volatile.Read(ref snapshots[index]), completedRows[index], work[index], index, settings);
        inspector.Activate(); return inspector;
    }

    internal void StopRun() => run?.Cancel();
    internal FractalSettings ReadConfiguration()
    {
        if (!int.TryParse(columnsEditor.Text, out int columns) || !int.TryParse(rowsEditor.Text, out int rows) ||
            !int.TryParse(iterationsEditor.Text, out int iterations) || !int.TryParse(delayEditor.Text, out int delay))
            throw new ArgumentException("Enter whole numbers for columns, rows, iterations and row delay.");
        var config = new FractalSettings(columns, rows, iterations, delay); config.Validate(); return config;
    }
    internal void StartRun()
    {
        if (RunTask is { IsCompleted: false } || closed) return;
        run?.Dispose(); run = new(); Result = null;
        Array.Clear(completedRows); Array.Clear(work); Array.Clear(cycles); Array.Clear(latestFrames); Array.Clear(snapshots);
        Array.Fill(framePixels, Palette[0]);
        for (int i = 0; i < screens.Length; i++)
        { screens[i].WritePixels(new Int32Rect(0, 0, 160, 144), framePixels, 160 * 4, 0); UpdateNode(i); }
        foreach (var (index, inspector) in inspectors)
            inspector.UpdateFractal(null, 0, 0, index, settings);
        while (received.TryDequeue(out _)) { }
        Array.Fill(pixels, unchecked((int)0xFF1B2B3B)); collected.WritePixels(new Int32Rect(0, 0, layout.Width, layout.Height), pixels, layout.Width * 4, 0);
        restart.IsEnabled = false; stop.IsEnabled = true; export.IsEnabled = false;
        status.Text = $"Running {layout.Workers} SDK-built Game Boys. Waiting for native row results over TCP…";
        clock.Restart();
        RunTask = RunClusterAsync(run.Token);
    }
    private async Task RunClusterAsync(CancellationToken token)
    {
        try
        {
            var result = await FractalCluster.RunAsync(row => received.Enqueue(row),
                (id, frame) => Interlocked.Exchange(ref latestFrames[id - 1], frame), token, configuration: settings,
                onSnapshot: snapshot => Interlocked.Exchange(ref snapshots[snapshot.NodeId - 1], snapshot));
            if (closed) return;
            Result = result; clock.Stop(); RefreshPresentation();
            foreach (var tile in result.Tiles) { cycles[tile.NodeId - 1] = tile.Cycles; UpdateNode(tile.NodeId - 1); }
            status.Text = $"Complete. All {pixels.Length:N0} pixels verified · {result.Tiles.Sum(t => t.Work):N0} native iterations · {result.ElapsedSeconds:F1}s. Collect values to inspect the data.";
            export.IsEnabled = true;
            restart.IsEnabled = true; stop.IsEnabled = false;
            if (capturePath != null)
            {
                ExportResults(Path.ChangeExtension(capturePath, ".json")); Capture(capturePath);
                if (captureAndClose) Close();
            }
        }
        catch (OperationCanceledException) { if (!closed) { clock.Stop(); status.Text = $"Stopped. Run again to start {layout.Workers} fresh machines."; } }
        catch (Exception ex) { if (!closed) { clock.Stop(); status.Text = "Cluster stopped: " + ex.Message; } }
        finally { if (!closed) { restart.IsEnabled = true; stop.IsEnabled = false; } }
    }
    private void RefreshPresentation()
    {
        for (int i = 0; i < layout.Workers; i++)
        {
            if (Interlocked.Exchange(ref latestFrames[i], null) is { } frame)
            {
                var source = frame.Pixels;
                for (int pixel = 0; pixel < framePixels.Length; pixel++) framePixels[pixel] = Palette[source[pixel]];
                screens[i].WritePixels(new Int32Rect(0, 0, 160, 144), framePixels, 160 * 4, 0);
                cycles[i] = frame.Cycles; UpdateNode(i);
            }
        }
        bool dirty = false;
        while (received.TryDequeue(out var row))
        {
            int index = row.NodeId - 1; completedRows[index] = row.Row + 1; work[index] += row.Work;
            for (int x = 0; x < 32; x++)
            {
                byte value = row.Iterations[x];
                pixels[(row.TileY * 24 + row.Row) * layout.Width + row.TileX * 32 + x] = Palette[value == settings.Iterations ? 3 : value & 3];
            }
            dirty = true; UpdateNode(index);
        }
        if (dirty) collected.WritePixels(new Int32Rect(0, 0, layout.Width, layout.Height), pixels, layout.Width * 4, 0);
        foreach (var (index, inspector) in inspectors)
            inspector.UpdateFractal(Volatile.Read(ref snapshots[index]), completedRows[index], work[index], index, settings);
        pixelCount.Text = $"{completedRows.Sum() * 32:N0} / {pixels.Length:N0}";
        iterationCount.Text = work.Sum().ToString("N0"); elapsed.Text = $"{clock.Elapsed.TotalSeconds:F1}s";
    }
    private void UpdateNode(int i)
    {
        bars[i].Value = completedRows[i];
        nodeStatus[i].Text = $"{(completedRows[i] == 24 ? "DONE" : completedRows[i] + "/24")} · {work[i]:N0} iters\n{cycles[i] / 1_000_000d:F2}M cycles";
    }
    internal void ExportResults(string path)
    {
        var result = Result ?? throw new InvalidOperationException("The cluster has not completed.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            result.Width, result.Height, result.MaximumIterations, result.ElapsedSeconds,
            Arithmetic = "Q5 / divide toward zero", Transport = "localhost TCP", layout.Workers, layout.Columns, layout.Rows, settings.RowDelayMs,
            TotalIterations = result.Tiles.Sum(t => t.Work), Pixels = result.Iterations.Select(v => (int)v).ToArray(),
            Nodes = result.Tiles.Select(t => new { t.NodeId, t.TileX, t.TileY, t.Cycles, t.Work, Inspection = Volatile.Read(ref snapshots[t.NodeId - 1]) })
        }, new JsonSerializerOptions { WriteIndented = true }));
        string stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var csv = new StringBuilder("node_id,x,y,iterations\n");
        for (int y = 0; y < result.Height; y++) for (int x = 0; x < result.Width; x++) csv.Append(CultureInfo.InvariantCulture, $"{y / 24 * layout.Columns + x / 32 + 1},{x},{y},{result.Iterations[y * result.Width + x]}\n");
        File.WriteAllText(stem + ".pixels.csv", csv.ToString());
        csv.Clear().Append("node_id,tile_x,tile_y,pixels,iterations,emulated_cycles\n");
        foreach (var tile in result.Tiles) csv.Append(CultureInfo.InvariantCulture, $"{tile.NodeId},{tile.TileX},{tile.TileY},768,{tile.Work},{tile.Cycles}\n");
        File.WriteAllText(stem + ".nodes.csv", csv.ToString());
        SavePng(collected, stem + ".image.png");
    }
    internal void Capture(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(Background, null, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render((Visual)Content); SavePng(bitmap, path);
    }
    private static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static TextBlock Text(string text, double size, string color, bool bold = false) =>
        new() { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    private static Button ActionButton(string text)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 8, 12, 8), Background = Brush("#24483F"), Foreground = Brush("#E6FFF6"), BorderBrush = Brush("#3C7567"), BorderThickness = new Thickness(1) };
        // A local template keeps native disabled-button chrome legible on the dark dashboard.
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4" /></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#315D51" /></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        return button;
    }
    private static StackPanel Metric(string label, TextBlock value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        panel.Children.Add(Text(label, 10, "#93A7BE", true)); panel.Children.Add(value); return panel;
    }
}
