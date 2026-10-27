using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
using Grid = System.Windows.Controls.Grid;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace GB.Experiments.Windows.Labs;

/// <summary>Native worker LCDs plus the validated world assembled from TCP cell records.</summary>
internal sealed class LifeClusterWindow : Window
{
    private static readonly int[] Palette = [unchecked((int)0xFFADB975), unchecked((int)0xFF829454), unchecked((int)0xFF435437), unchecked((int)0xFF1E3026)];
    private readonly LifeSettings settings;
    private readonly Dictionary<int, WriteableBitmap> lcds = new();
    private readonly Dictionary<int, VideoFrame> rendered = new();
    private readonly ConcurrentDictionary<ushort, byte> inspectedNodes = new();
    private readonly UniformGrid tiles;
    private readonly TextBlock pageLabel = Text("", 12, "#6FE6CA");
    private readonly Button previous = Action("Previous"), nextPage = Action("Next");
    private readonly int nodeCount, pageColumns;
    private const int PageSize = 16;
    private int pageIndex, visibleStart, visibleEnd;
    private readonly VideoFrame?[] frames;
    private readonly NativeNodeSnapshot?[] snapshots;
    private readonly Dictionary<int, NetworkNodeWindow> inspectors = new();
    private readonly ConcurrentQueue<LifeGeneration> incoming = new();
    private readonly List<TickMetric> history = new();
    private readonly WriteableBitmap world;
    private readonly int[] lcdPixels = new int[160 * 144], worldPixels;
    private readonly TextBlock status = Text("Preparing machines…", 13, "#9BB0C6"), metrics = Text("", 14, "#9BB0C6");
    private readonly Button again = Action("Run again"), stop = Action("Stop"), export = Action("Collect values");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private CancellationTokenSource? lifetime;
    private bool closed;
    private int generation;
    private readonly string? capture;
    private readonly bool captureAndClose;
    internal LifeClusterResult? Result { get; private set; }
    internal Task? RunTask { get; private set; }
    internal LifeSettings Configuration => settings;
    private sealed record TickMetric(int Generation, int Alive, double ElapsedSeconds);

    internal LifeClusterWindow(string[] args)
    {
        var options = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--capture" && i + 1 < args.Length) capture = Path.GetFullPath(args[++i]);
            else if (args[i] == "--capture-and-close") captureAndClose = true;
            else options.Add(args[i]);
        }
        settings = LifeSettings.Parse(options.ToArray()); var layout = settings.Layout;
        nodeCount = layout.Workers; pageColumns = Math.Min(4, layout.Columns); frames = new VideoFrame?[layout.Workers]; snapshots = new NativeNodeSnapshot?[layout.Workers];
        world = new(layout.Width, layout.Height, 96, 96, PixelFormats.Bgra32, null); worldPixels = new int[layout.Width * layout.Height];
        Title = $"SharpBoy · Living world · {layout.Workers} Game Boys";
        Width = Math.Min(1650, SystemParameters.WorkArea.Width - 30); Height = Math.Min(1000, SystemParameters.WorkArea.Height - 40);
        MinWidth = 1000; MinHeight = 700; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#09121F"); FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        var root = new Grid { Margin = new Thickness(24), Background = Background };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(Text($"{layout.Workers} Game Boys. One living world.", 30, "#F0F5FA", true));
        header.Children.Add(Text($"{layout.Workers} native Game Boys · {layout.Width} × {layout.Height} cells · Conway B3/S23 · {(settings.HostsFile == null ? "local TCP" : "distributed TCP over SSH")}", 13, "#6FE6CA")); root.Children.Add(header);
        var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1.5, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 350 });
        tiles = new UniformGrid { Columns = pageColumns, Rows = Math.Min(4, (Math.Min(PageSize, nodeCount) + pageColumns - 1) / pageColumns),
            MinWidth = pageColumns * 120, MinHeight = Math.Min(4, (Math.Min(PageSize, nodeCount) + pageColumns - 1) / pageColumns) * 130 };
        var scroll = new ScrollViewer { Content = tiles, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0) return;
            tiles.Width = Math.Max(tiles.MinWidth, scroll.ViewportWidth); tiles.Height = Math.Max(tiles.MinHeight, scroll.ViewportHeight);
        };
        var left = new DockPanel { Margin = new Thickness(0, 0, 20, 0) };
        var navigation = new WrapPanel { Margin = new Thickness(4, 0, 0, 6) };
        navigation.Children.Add(previous); navigation.Children.Add(nextPage); pageLabel.Margin = new Thickness(6, 10, 14, 0); navigation.Children.Add(pageLabel);
        var inspectId = new TextBox { Text = "1", Width = 65, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8),
            Background = Brush("#172B3F"), Foreground = Brush("#F0F5FA") };
        var inspect = Action("Inspect GB"); navigation.Children.Add(inspectId); navigation.Children.Add(inspect);
        inspect.Click += (_, _) =>
        {
            if (int.TryParse(inspectId.Text, out int id) && id >= 1 && id <= nodeCount) OpenNodeDetails(id - 1);
            else status.Text = $"Enter a Game Boy ID from 1 to {nodeCount}.";
        };
        previous.Click += (_, _) => ChangePage(pageIndex - 1); nextPage.Click += (_, _) => ChangePage(pageIndex + 1);
        DockPanel.SetDock(navigation, Dock.Top); left.Children.Add(navigation); left.Children.Add(scroll); body.Children.Add(left);
        ChangePage(0);
        var right = new StackPanel { Margin = new Thickness(20) };
        right.Children.Add(Text("THE LIVING WORLD", 12, "#6FE6CA", true));
        var note = Text("Every territory evolves inside its Game Boy. Each tick exchanges a one-cell halo, including diagonal neighbours. Lines mark machine boundaries.", 13, "#9BB0C6"); note.Margin = new Thickness(0, 10, 0, 16); right.Children.Add(note);
        var picture = new Grid { Width = 440, Height = 440d * layout.Height / layout.Width };
        var image = new Image { Source = world, Stretch = Stretch.Uniform, Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Click a territory to inspect its Game Boy" };
        image.MouseLeftButtonUp += (_, e) =>
        {
            var point = e.GetPosition(image);
            int x = Math.Clamp((int)(point.X / image.ActualWidth * settings.Columns), 0, settings.Columns - 1);
            int y = Math.Clamp((int)(point.Y / image.ActualHeight * settings.Rows), 0, settings.Rows - 1);
            int index = y * settings.Columns + x; ChangePage(index / PageSize); OpenNodeDetails(index);
        }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); picture.Children.Add(image);
        var lines = new UniformGrid { Columns = layout.Columns, Rows = layout.Rows, IsHitTestVisible = false };
        for (int i = 0; i < layout.Workers; i++) lines.Children.Add(new Border { BorderBrush = Brush("#547657"), BorderThickness = new Thickness(.5) }); picture.Children.Add(lines);
        right.Children.Add(new Viewbox { Child = picture, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        metrics.Margin = new Thickness(0, 18, 0, 18); right.Children.Add(metrics);
        var controls = new WrapPanel(); controls.Children.Add(again); controls.Children.Add(stop); controls.Children.Add(export); right.Children.Add(controls);
        again.Click += (_, _) => StartRun(); stop.Click += (_, _) => StopRun();
        export.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Life results|*.json", FileName = "living-world.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { ExportResults(dialog.FileName); status.Text = "Saved final cells, node inspection, generation CSV and world PNG."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = ex.Message; }
        };
        right.Children.Add(Text("CONFIGURE A NEW WORLD", 11, "#6FE6CA", true));
        var editors = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        TextBox Field(string label, int value)
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 10, 8) }; group.Children.Add(Text(label, 10, "#9BB0C6"));
            var field = new TextBox { Text = value.ToString(), Width = label == "Seed" ? 100 : 65, Padding = new Thickness(6), Background = Brush("#172B3F"), Foreground = Brush("#F0F5FA") };
            group.Children.Add(field); editors.Children.Add(group); return field;
        }
        var columns = Field("Columns", settings.Columns); var rows = Field("Rows", settings.Rows); var generations = Field("Ticks", settings.Generations);
        var delay = Field("Delay ms", settings.TickDelayMs); var seed = Field("Seed", settings.Seed); var processes = Field("Processes", settings.WorkerProcesses); right.Children.Add(editors);
        right.Children.Add(Text("Hosts JSON (optional; overrides Processes)", 11, "#9BB0C6"));
        var hosts = new TextBox { Text = settings.HostsFile ?? "", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 8),
            Background = Brush("#172B3F"), Foreground = Brush("#F0F5FA") };
        right.Children.Add(hosts);
        var apply = Action("Apply & run"); right.Children.Add(apply);
        apply.Click += (_, _) =>
        {
            try
            {
                string[] nextArgs = ["--columns", columns.Text, "--rows", rows.Text, "--generations", generations.Text, "--tick-delay-ms", delay.Text, "--seed", seed.Text, "--processes", processes.Text];
                if (!string.IsNullOrWhiteSpace(hosts.Text)) nextArgs = [.. nextArgs, "--hosts", hosts.Text];
                _ = LifeSettings.Parse(nextArgs); var next = new LifeClusterWindow(nextArgs); next.Show(); Close();
            }
            catch (ArgumentException ex) { status.Text = ex.Message.Split('\n')[0]; }
        };
        right.Children.Add(Text("Up to 1,000 machines · 1–1,000 ticks\nSeeded world wraps at all four edges.\nEvery returned cell is independently verified.\nProcesses: -1 auto, 0 in-process, 1-32 groups.\nPage through LCDs or inspect a machine by ID.", 12, "#9BB0C6"));
        var collector = new Border { Child = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Background = Brush("#101D2D"), CornerRadius = new CornerRadius(14) };
        Grid.SetColumn(collector, 1); body.Children.Add(collector);
        status.Margin = new Thickness(0, 15, 0, 0); Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => { timer.Start(); StartRun(); };
        Closed += (_, _) => { closed = true; timer.Stop(); lifetime?.Cancel(); };
    }
    internal int ActivePage => pageIndex;
    internal int AllocatedLcds => lcds.Count;
    internal void ChangePage(int page)
    {
        if (page < 0 || page > (nodeCount - 1) / PageSize) throw new ArgumentOutOfRangeException(nameof(page));
        pageIndex = page; int start = page * PageSize, end = Math.Min(nodeCount, start + PageSize);
        Volatile.Write(ref visibleStart, start); Volatile.Write(ref visibleEnd, end);
        foreach (int index in lcds.Keys.ToArray())
            if ((index < start || index >= end) && !inspectors.ContainsKey(index)) { lcds.Remove(index); rendered.Remove(index); }
        tiles.Children.Clear();
        for (int index = start; index < end; index++)
        {
            int selected = index;
            var panel = new Grid { Margin = new Thickness(10) }; panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new());
            panel.Children.Add(Text($"GB {index + 1:0000}  /  {index % settings.Columns + 1}:{index / settings.Columns + 1}", 11, "#6FE6CA", true));
            var lcd = new Image { Source = GetScreen(index), Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Inspect this Game Boy", Focusable = true };
            RenderOptions.SetBitmapScalingMode(lcd, BitmapScalingMode.NearestNeighbor);
            lcd.MouseLeftButtonUp += (_, _) => OpenNodeDetails(selected);
            lcd.KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space) { OpenNodeDetails(selected); e.Handled = true; } };
            Grid.SetRow(lcd, 1); panel.Children.Add(lcd);
            tiles.Children.Add(new Border { Child = panel, Background = Brush("#111E2E"), CornerRadius = new CornerRadius(10), Margin = new Thickness(4) });
        }
        pageLabel.Text = $"{start + 1}-{end} / {nodeCount}";
        previous.IsEnabled = page > 0; nextPage.IsEnabled = end < nodeCount;
    }
    private WriteableBitmap GetScreen(int index)
    {
        if (!lcds.TryGetValue(index, out var screen))
        {
            screen = new(160, 144, 96, 96, PixelFormats.Bgra32, null); lcds.Add(index, screen);
            if (Volatile.Read(ref frames[index]) is { } frame) Render(index, screen, frame);
            else { Array.Fill(lcdPixels, Palette[0]); screen.WritePixels(new Int32Rect(0, 0, 160, 144), lcdPixels, 640, 0); }
        }
        return screen;
    }
    private void Render(int index, WriteableBitmap screen, VideoFrame frame)
    {
        for (int p = 0; p < lcdPixels.Length; p++) lcdPixels[p] = Palette[frame.Pixels[p]];
        screen.WritePixels(new Int32Rect(0, 0, 160, 144), lcdPixels, 640, 0); rendered[index] = frame;
    }
    internal NetworkNodeWindow OpenNodeDetails(int index)
    {
        if ((uint)index >= (uint)nodeCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (!inspectors.TryGetValue(index, out var inspector))
        {
            inspector = new(index, GetScreen(index)) { Owner = this };
            inspectors.Add(index, inspector); inspectedNodes[(ushort)(index + 1)] = 0;
            inspector.Closed += (_, _) =>
            {
                inspectors.Remove(index); inspectedNodes.TryRemove((ushort)(index + 1), out _);
                if (index < visibleStart || index >= visibleEnd) { lcds.Remove(index); rendered.Remove(index); }
            };
            inspector.Show();
        }
        inspector.UpdateLife(Volatile.Read(ref snapshots[index]), settings, generation, index); inspector.Activate(); return inspector;
    }
    internal void StopRun() => lifetime?.Cancel();
    internal void StartRun()
    {
        if (closed || RunTask is { IsCompleted: false }) return;
        lifetime?.Dispose(); lifetime = new(); Result = null; generation = 0; history.Clear();
        while (incoming.TryDequeue(out _)) { }
        Array.Clear(frames); Array.Clear(snapshots);
        Array.Fill(lcdPixels, Palette[0]); rendered.Clear(); foreach (var lcd in lcds.Values) lcd.WritePixels(new Int32Rect(0, 0, 160, 144), lcdPixels, 640, 0);
        Array.Fill(worldPixels, Palette[0]); world.WritePixels(new Int32Rect(0, 0, world.PixelWidth, world.PixelHeight), worldPixels, world.PixelWidth * 4, 0);
        foreach (var (index, inspector) in inspectors) inspector.UpdateLife(null, settings, 0, index);
        again.IsEnabled = export.IsEnabled = false; stop.IsEnabled = true; metrics.Text = "Waiting for native workers…";
        status.Text = "Connecting Game Boys. Generation barriers advance only after every territory passes validation.";
        RunTask = RunAsync(lifetime.Token);
    }
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            Result = await Task.Run(() => LifeCluster.RunAsync(settings, incoming.Enqueue,
                (id, frame) => Interlocked.Exchange(ref frames[id - 1], frame),
                snapshot => Interlocked.Exchange(ref snapshots[snapshot.NodeId - 1], snapshot), token,
                wantsFrames: id => id - 1 >= Volatile.Read(ref visibleStart) && id - 1 < Volatile.Read(ref visibleEnd) || inspectedNodes.ContainsKey(id)));
            if (closed) return;
            Refresh(); export.IsEnabled = again.IsEnabled = true; stop.IsEnabled = false; status.Text = $"Complete · {generation} verified generations · {nodeCount} native machines · all TCP sockets closed.";
            if (capture != null) { ExportResults(Path.ChangeExtension(capture, ".json")); Capture(capture); if (captureAndClose) Close(); }
        }
        catch (OperationCanceledException) { if (!closed) { Refresh(); status.Text = "Stopped · final received generation retained."; } }
        catch (Exception ex) { if (!closed) { Refresh(); status.Text = "Experiment failed: " + ex.Message; } }
        finally { if (!closed) { again.IsEnabled = true; stop.IsEnabled = false; } }
    }
    private void Refresh()
    {
        foreach (var (index, screen) in lcds)
        {
            if (Volatile.Read(ref frames[index]) is not { } frame || rendered.TryGetValue(index, out var old) && ReferenceEquals(old, frame)) continue;
            Render(index, screen, frame);
        }
        while (incoming.TryDequeue(out var tick))
        {
            generation = tick.Generation; history.Add(new(tick.Generation, tick.Alive, tick.ElapsedSeconds));
            for (int i = 0; i < worldPixels.Length; i++) worldPixels[i] = Palette[tick.Cells.Span[i] == 0 ? 0 : 3];
            world.WritePixels(new Int32Rect(0, 0, tick.Width, tick.Height), worldPixels, tick.Width * 4, 0);
            metrics.Text = $"GENERATION {tick.Generation} / {settings.Generations}\n{tick.Alive:N0} living cells / {worldPixels.Length:N0}\n{tick.ElapsedSeconds:F1}s wall time · {settings.TickDelayMs}ms deliberate tick delay\n{tick.WorkerProcesses} worker processes / {tick.WorkerMemoryBytes / 1048576d:F0} MiB worker RAM at barrier";
        }
        foreach (var (index, inspector) in inspectors) inspector.UpdateLife(Volatile.Read(ref snapshots[index]), settings, generation, index);
    }
    internal void ExportResults(string path)
    {
        var result = Result ?? throw new InvalidOperationException("Wait for the experiment to complete.");
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Settings = settings, Transport = settings.HostsFile == null ? "localhost TCP / routed halo" : "TCP over SSH / routed halo", Rule = "B3/S23", Boundary = "toroidal",
            result.Final.Generation, result.Final.Width, result.Final.Height, result.Final.Alive, result.Final.ElapsedSeconds,
            Cells = result.Final.Cells.ToArray().Select(v => (int)v), Nodes = snapshots, result.Metrics, History = history }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.ChangeExtension(path, ".generations.csv"), new[] { "generation,alive,elapsed_seconds" }.Concat(history.Select(t => FormattableString.Invariant($"{t.Generation},{t.Alive},{t.ElapsedSeconds:F6}"))));
        SavePng(world, Path.ChangeExtension(path, ".world.png"));
    }
    internal void Capture(string path)
    {
        UpdateLayout(); var content = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(content), null, new System.Windows.Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(visual); SavePng(bitmap, path);
    }
    private static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
    private static SolidColorBrush Brush(string value) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
    private static TextBlock Text(string value, double size, string color, bool bold = false) => new() { Text = value, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    private static Button Action(string text)
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
}
