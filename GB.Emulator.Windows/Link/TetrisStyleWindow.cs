using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GB.Emulator.AI.Tetris;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace GB.Emulator.Windows.Link;

/// <summary>Live per-opponent policy editor. No emulator or network state is owned here.</summary>
internal sealed class TetrisStyleWindow : Window
{
    private readonly Action<TetrisAiSettings> apply;
    private readonly ComboBox presets = new() { Width = 190, Height = 30, HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        Background = Brushes.Beige, Foreground = Brushes.Black };
    private readonly TextBlock message = Words("Changes apply live at the next decision. Save a style to keep it between sessions.", 12);
    private readonly Dictionary<string, (Slider Slider, TextBlock Value, Func<TetrisAiSettings, double> Read)> knobs = new();
    private bool syncing;
    internal TetrisAiSettings Settings { get; private set; }
    internal IReadOnlyDictionary<string, Slider> Sliders => knobs.ToDictionary(p => p.Key, p => p.Value.Slider);

    internal TetrisStyleWindow(TetrisAiSettings settings, Action<TetrisAiSettings> apply)
    {
        settings.Validate(); Settings = settings; this.apply = apply;
        Title = "SharpBoy · AI playing style"; Width = 520; Height = 790; MinWidth = 460; MinHeight = 560;
        Background = new SolidColorBrush(Color.FromRgb(23, 27, 25)); Foreground = Brushes.White;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24), Background = Background };
        var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(Words("PLAYER TWO / PLAYING STYLE", 21));
        header.Children.Add(Words("Shape the decisions, then watch the Game Boy play them.", 13));
        presets.ItemsSource = new[] { "Custom" }.Concat(TetrisAiSettings.Presets.Keys).ToArray();
        presets.Margin = new Thickness(0, 14, 0, 14); header.Children.Add(presets);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var files = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) };
        var save = new Button { Content = "Save style…", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 10, 0) };
        var load = new Button { Content = "Load style…", Padding = new Thickness(14, 7, 14, 7) };
        files.Children.Add(save); files.Children.Add(load); footer.Children.Add(files); footer.Children.Add(message);
        var body = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        Add("ClearLines", "Clear lines", "Reward immediate line clears. Higher values favour scoring now.", 4, s => s.ClearLines, (s, v) => s with { ClearLines = v });
        Add("TetrisBonus", "Chase four-line clears", "Extra reward for a Tetris. High values encourage waiting for four-line opportunities.", 8, s => s.TetrisBonus, (s, v) => s with { TetrisBonus = v });
        Add("AvoidHoles", "Avoid buried holes", "Penalise empty cells beneath blocks. Usually the most useful survival control.", 8, s => s.AvoidHoles, (s, v) => s with { AvoidHoles = v });
        Add("LowStack", "Keep the stack low", "Prefer lower total column height over ambitious stacks.", 2, s => s.LowStack, (s, v) => s with { LowStack = v });
        Add("FlatSurface", "Keep the surface flat", "Penalise differences between neighbouring column heights.", 2, s => s.FlatSurface, (s, v) => s with { FlatSurface = v });
        Add("CeilingSafety", "Protect headroom", "Increasing penalty above 12 rows. Favour emergency survival near the ceiling.", 4, s => s.CeilingSafety, (s, v) => s with { CeilingSafety = v });
        Add("Lookahead", "Plan for the next piece", "Blend in the best reachable placement of the visible preview. Zero disables lookahead.", 1, s => s.Lookahead, (s, v) => s with { Lookahead = v });
        Add("ReleaseFrames", "Reaction delay", "Minimum guest frames released between taps. Lower is faster; Game Boy speed stays the same.", 20, s => s.ReleaseFrames, (s, v) => s with { ReleaseFrames = (int)v }, integer: true);
        Add("HoldFrames", "Button hold", "Guest frames each button stays down. Short pulses are quicker; long holds may repeat in the ROM.", 4, s => s.HoldFrames, (s, v) => s with { HoldFrames = (int)v }, integer: true);
        Content = root;
        presets.SelectionChanged += (_, _) =>
        {
            if (!syncing && presets.SelectedItem is string name && TetrisAiSettings.Presets.TryGetValue(name, out var preset)) SetSettings(preset);
        };
        save.Click += (_, _) => Save(); load.Click += (_, _) => Load();
        Refresh();

        void Add(string id, string label, string help, double maximum, Func<TetrisAiSettings, double> read,
            Func<TetrisAiSettings, double, TetrisAiSettings> change, bool integer = false)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 12, 0) };
            row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Words(label, 14));
            var value = Words("", 13); Grid.SetColumn(value, 1); row.Children.Add(value); body.Children.Add(row);
            body.Children.Add(Words(help, 11));
            var slider = new Slider { Minimum = integer ? 1 : 0, Maximum = maximum, Value = read(settings),
                TickFrequency = integer ? 1 : .05, IsSnapToTickEnabled = integer, SmallChange = integer ? 1 : .05,
                LargeChange = integer ? 2 : .25, IsMoveToPointEnabled = true, ToolTip = help, Margin = new Thickness(0, 3, 12, 10) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(slider, id);
            knobs.Add(id, (slider, value, read)); body.Children.Add(slider);
            slider.ValueChanged += (_, _) =>
            {
                if (syncing) return;
                Settings = change(Settings, integer ? Math.Round(slider.Value) : Math.Round(slider.Value, 2));
                Settings.Validate(); Refresh(); apply(Settings);
                message.Text = "Style updated. The next decision uses these settings.";
            };
        }
    }

    internal void SetSettings(TetrisAiSettings settings)
    {
        settings.Validate(); Settings = settings; Refresh(); apply(settings);
        message.Text = "Style updated. The next decision uses these settings.";
    }

    private void Refresh()
    {
        syncing = true;
        try
        {
            foreach (var (id, knob) in knobs)
            {
                double value = knob.Read(Settings); knob.Slider.Value = value;
                knob.Value.Text = id.EndsWith("Frames", StringComparison.Ordinal) ? $"{value:0} frames / {value * 16.743:F0} ms" :
                    id == "Lookahead" ? $"{value:P0}" : value.ToString("F2");
            }
            presets.SelectedItem = TetrisAiSettings.Presets.FirstOrDefault(p => p.Value == Settings).Key ?? "Custom";
        }
        finally { syncing = false; }
    }

    private void Save()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "AI playing style (*.json)|*.json", FileName = "tetris-ai-style.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { Settings.Save(dialog.FileName); message.Text = "Playing style saved."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { message.Text = ex.Message; }
    }

    private void Load()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "AI playing style (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { SetSettings(TetrisAiSettings.Load(dialog.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { message.Text = ex.Message; }
    }

    private static TextBlock Words(string text, double size) => new() { Text = text, FontSize = size,
        Foreground = size >= 14 ? Brushes.Beige : Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
}
