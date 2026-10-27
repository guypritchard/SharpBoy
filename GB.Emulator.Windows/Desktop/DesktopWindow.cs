using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Core.Win32;
using GB.Emulator.Hosting;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using MenuItem = System.Windows.Controls.MenuItem;

namespace GB.Emulator.Windows.Desktop;

/// <summary>Transparent native desktop presentation; machine state and pacing remain in EmulationHost.</summary>
internal sealed class DesktopWindow : Window
{
    private readonly EmulationHost host = new(audio: WindowsAudioOutput.TryCreate(), options: new() { HistoryCapacity = 0 });
    private readonly DesktopScene scene = new();
    private readonly Grid surface = new();
    private readonly System.Windows.Controls.Image closeUp;
    private readonly ScreenTransitionView transitionView;
    private Point[] transitionCorners = [];
    private double transitionStarted, transitionCenterY;
    internal bool IsViewTransitioning { get; private set; }
    private readonly TextBlock hint = new();
    private readonly Border hintPanel;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Key> heldKeys = [];
    private readonly string? initialRom, capturePath;
    private readonly double? initialYaw, initialPitch;
    private readonly bool captureAndClose;
    private readonly bool animateViewTransitions;
    private VideoFrame? presented;
    private GameBoyButton mouseButton;
    private Point lastMouse;
    private bool rotating, loading, closed, faultShown, captured;
    private long lastTick;
    private double hintUntil = 8, scale = 1;
    internal EmulationHost Host => host;
    internal DesktopScene Scene => scene;
    internal bool IsRotating => rotating;
    internal bool IsScreenCloseUp { get; private set; }

    public DesktopWindow(string[] args, bool animateViewTransitions = true)
    {
        this.animateViewTransitions = animateViewTransitions;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--capture" when i+1 < args.Length: capturePath = Path.GetFullPath(args[++i]); break;
                case "--capture-and-close": captureAndClose = true; break;
                case "--yaw" when i+1 < args.Length: initialYaw = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--pitch" when i+1 < args.Length: initialPitch = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                default:
                    if (args[i].StartsWith("--") || initialRom != null) throw new ArgumentException("Usage: --desktop [ROM.gb]");
                    initialRom = args[i]; break;
            }
        }
        Title = "SharpBoy · Desktop Game Boy";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; Width = 640; Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AllowDrop = true; Focusable = true;
        surface.Children.Add(scene.View);
        closeUp = new System.Windows.Controls.Image { Source = scene.ScreenImage, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(closeUp, BitmapScalingMode.NearestNeighbor);
        surface.Children.Add(closeUp);
        transitionView = new ScreenTransitionView(scene.ScreenImage);
        surface.Children.Add(transitionView);
        hint.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); hint.FontSize = 12;
        hint.Foreground = new SolidColorBrush(Color.FromRgb(232, 233, 225));
        hint.TextAlignment = TextAlignment.Center; hint.TextWrapping = TextWrapping.Wrap;
        hint.Text = "Ctrl + drag to rotate  ·  Drag shell to move\nDouble-click LCD for close-up  ·  Click power switch to exit\nScroll to resize  ·  Ctrl+O to load  ·  Right-click for controls";
        hintPanel = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 22, 26, 29)), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(15, 10, 15, 10), Child = hint, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 20), IsHitTestVisible = false };
        surface.Children.Add(hintPanel); Content = surface;
        BuildMenu();
        PreviewKeyDown += KeyDownHandler; PreviewKeyUp += KeyUpHandler;
        MouseLeftButtonDown += MouseDownHandler; MouseLeftButtonUp += MouseUpHandler;
        MouseMove += MouseMoveHandler;
        LostMouseCapture += (_, _) => { rotating = false; mouseButton = 0; Cursor = null; UpdateButtons(); };
        MouseWheel += (_, e) => { SetScale(scale * (e.Delta > 0 ? 1.08 : 1/1.08)); e.Handled = true; };
        Deactivated += (_, _) => { EndMouse(); ReleaseButtons(); };
        DragEnter += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) await LoadRomAsync(files[0]);
        };
        timer.Tick += (_, _) => Tick();
        Loaded += async (_, _) =>
        {
            SetScale(Math.Min(1, (SystemParameters.WorkArea.Height - 35)/780));
            if (initialYaw.HasValue || initialPitch.HasValue) scene.SetView(initialYaw ?? scene.Yaw, initialPitch ?? scene.Pitch);
            timer.Start(); Focus();
            if (initialRom != null) await LoadRomAsync(initialRom);
        };
        Closed += (_, _) =>
        {
            timer.Stop(); lifetime.Cancel(); ReleaseButtons(); closed = true; host.Dispose(); lifetime.Dispose();
        };
    }

    private void BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        MenuItem Item(string label, Action action)
        {
            var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); return item;
        }
        Item("Open ROM…    Ctrl+O", async () => await ChooseRomAsync());
        Item("Load bundled demo", async () => await LoadRomAsync(Path.Combine(AppContext.BaseDirectory, "SPRITE.GB")));
        menu.Items.Add(new Separator());
        Item("Screen close-up / 3D    Double-click LCD", ToggleScreenCloseUp);
        Item("Pause / resume    Ctrl+P", TogglePause);
        Item("Reset cartridge    Ctrl+R", () => { if (host.Inspection.CaptureCartridge() != null) { host.Reset(); host.Resume(); } });
        var mute = Item("Mute audio", () => host.Muted = !host.Muted); mute.IsCheckable = true;
        var top = Item("Keep above other windows", () => Topmost = !Topmost); top.IsCheckable = true;
        menu.Items.Add(new Separator());
        Item("Front view", () => scene.SetView(0, 0));
        Item("Rear / cartridge view", () => scene.SetView(180, 0));
        Item("Reset orientation    Home", scene.ResetView);
        Item("Small", () => SetScale(.75)); Item("Medium", () => SetScale(1)); Item("Large", () => SetScale(1.35));
        Item("Controls / artwork", () => ShowHint("Arrows: D-pad · Z/X: A/B · Enter/Space: Start/Select\nLabel artwork: put <ROM name>.label.png beside your ROM.", 10));
        Item("Model: Lokeig · CC BY-NC 4.0", () => ShowHint("Nintendo Game Boy (Original 1989) by Lokeig\nCC BY-NC 4.0 · see Desktop/Assets/ATTRIBUTION.md", 10));
        menu.Items.Add(new Separator()); Item("Exit    Alt+F4", Close);
        menu.Opened += (_, _) => { ReleaseButtons(); host.SetSuspended(PauseReason.Menu, true); };
        menu.Closed += (_, _) => { if (!closed) { host.SetSuspended(PauseReason.Menu, false); Focus(); } };
        ContextMenu = menu;
    }
    private async Task ChooseRomAsync()
    {
        if (loading || closed) return;
        ReleaseButtons(); host.SetSuspended(PauseReason.Dialog, true);
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Insert a Game Boy cartridge", Filter = "Game Boy ROMs|*.gb;*.gbc|All files|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) await LoadRomAsync(dialog.FileName);
        }
        finally { if (!closed) host.SetSuspended(PauseReason.Dialog, false); }
    }
    internal async Task LoadRomAsync(string path)
    {
        if (loading || closed) return;
        loading = true; ReleaseButtons();
        try
        {
            await host.LoadAsync(path, lifetime.Token);
            if (closed) return;
            var cartridge = host.Inspection.CaptureCartridge()!;
            var appearance = CartridgeAppearance.Read(path, cartridge.Title);
            scene.SetCartridge(appearance); Title = appearance.Title + " · SharpBoy Desktop";
            presented = null; faultShown = false; host.Resume();
            ShowHint(appearance.Title + (appearance.Label == null ? "\nTitle label · add a .label.png for the original artwork" : "\nMatching cartridge artwork loaded"), 5);
        }
        catch (OperationCanceledException) when (closed) { }
        catch (Exception ex) { if (!closed) ShowHint("Could not load ROM: " + ex.Message, 12); }
        finally { loading = false; }
    }
    private void Tick()
    {
        if (closed) return;
        if (IsViewTransitioning) AdvanceViewTransition();
        host.SetSuspended(PauseReason.Minimized, WindowState == WindowState.Minimized);
        long now = clock.ElapsedTicks;
        host.Advance(TimeSpan.FromSeconds((now - lastTick) / (double)Stopwatch.Frequency)); lastTick = now;
        if (host.LatestFrame is { } frame && !ReferenceEquals(frame, presented)) { scene.Present(frame); presented = frame; }
        if (host.State == EmulationState.Faulted && !faultShown) { faultShown = true; ShowHint("Emulation stopped: " + host.Fault?.Message, 20); }
        double target = clock.Elapsed.TotalSeconds < hintUntil ? 1 : 0;
        if (Math.Abs(hintPanel.Opacity - target) > .001)
            hintPanel.Opacity += (target - hintPanel.Opacity) * .15;
        if (capturePath != null && !captured && !loading && clock.Elapsed.TotalSeconds > 7)
        {
            captured = true; hintPanel.Opacity = 0;
            Capture(capturePath);
            if (captureAndClose) Close();
        }
    }
    internal void Capture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        File.WriteAllText(path + ".json", System.Text.Json.JsonSerializer.Serialize(new
        {
            State = host.State.ToString(), Cartridge = scene.Cartridge?.Title, scene.Yaw, scene.Pitch,
            scene.FrameNumber, host.Statistics, ModelTriangles = scene.TriangleCount,
            RenderingTier = RenderCapability.Tier >> 16, Transparent = AllowsTransparency
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path); encoder.Save(stream);
    }
    internal void SetScale(double value)
    {
        if (IsViewTransitioning) return;
        double baseHeight = IsScreenCloseUp ? 576 : 780;
        double limited = Math.Max(.4, Math.Min(1.7, Math.Min(SystemParameters.WorkArea.Width / 640, SystemParameters.WorkArea.Height / baseHeight)));
        scale = Math.Clamp(value, .4, limited);
        double cx = Left + Width/2, cy = Top + Height/2;
        Width = 640 * scale; Height = baseHeight * scale;
        if (IsLoaded) { Left = cx - Width/2; Top = cy - Height/2; }
    }
    internal void ToggleScreenCloseUp()
    {
        if (closed || IsViewTransitioning) return;
        EndMouse(); ReleaseButtons();
        IsScreenCloseUp = !IsScreenCloseUp;
        hintUntil = 0; hintPanel.Opacity = 0;
        if (!animateViewTransitions) { FinishViewTransition(); return; }
        transitionCenterY = Top + Height / 2;
        scene.View.Width = Width; scene.View.Height = 780 * scale;
        scene.View.Visibility = Visibility.Visible;
        surface.UpdateLayout();
        transitionCorners = scene.ProjectScreenCorners();
        closeUp.Visibility = Visibility.Collapsed;
        transitionView.Visibility = Visibility.Visible;
        transitionStarted = clock.Elapsed.TotalSeconds;
        IsViewTransitioning = true;
        AdvanceViewTransition();
    }
    private void AdvanceViewTransition()
    {
        double elapsed = Math.Clamp((clock.Elapsed.TotalSeconds - transitionStarted) / .38, 0, 1);
        double eased = elapsed * elapsed * (3 - 2 * elapsed);
        double progress = IsScreenCloseUp ? eased : 1 - eased;
        Height = (780 - 204 * progress) * scale;
        Top = transitionCenterY - Height / 2;
        scene.View.Opacity = 1 - progress;
        transitionView.Update(transitionCorners, Width, Height, 780 * scale, progress);
        if (elapsed >= 1) FinishViewTransition();
    }
    private void FinishViewTransition()
    {
        IsViewTransitioning = false;
        transitionView.Visibility = Visibility.Collapsed;
        scene.View.Width = double.NaN; scene.View.Height = double.NaN; scene.View.Opacity = 1;
        scene.View.Visibility = IsScreenCloseUp ? Visibility.Collapsed : Visibility.Visible;
        closeUp.Visibility = IsScreenCloseUp ? Visibility.Visible : Visibility.Collapsed;
        SetScale(scale);
    }
    private void ShowHint(string text, double seconds = 5) { hint.Text = text; hintUntil = clock.Elapsed.TotalSeconds + seconds; hintPanel.Opacity = 1; }
    private void TogglePause()
    {
        if (host.Inspection.CaptureCartridge() == null) { ShowHint("Open a ROM first · Ctrl+O"); return; }
        if (host.State == EmulationState.Faulted) { ShowHint("Reset the cartridge with Ctrl+R before resuming."); return; }
        ReleaseButtons();
        if (host.IsPlaybackRequested) host.Pause(); else host.Resume();
    }
    private void KeyDownHandler(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.O) { _ = ChooseRomAsync(); e.Handled = true; }
            if (e.Key == Key.P) { if (!e.IsRepeat) TogglePause(); e.Handled = true; }
            if (e.Key == Key.R) { if (!e.IsRepeat && host.Inspection.CaptureCartridge() != null) { host.Reset(); host.Resume(); } e.Handled = true; }
            return;
        }
        if (e.Key == Key.Home) { scene.ResetView(); e.Handled = true; }
        if (MapKey(e.Key) != 0) { heldKeys.Add(e.Key); UpdateButtons(); e.Handled = true; }
    }
    private void KeyUpHandler(object sender, KeyEventArgs e) { if (heldKeys.Remove(e.Key)) { UpdateButtons(); e.Handled = true; } }
    private void MouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (IsViewTransitioning) { e.Handled = true; return; }
        Focus(); lastMouse = e.GetPosition(this);
        if (!IsScreenCloseUp && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ReleaseButtons(); rotating = true; CaptureMouse(); Cursor = System.Windows.Input.Cursors.SizeAll; e.Handled = true; return;
        }
        if (HandleSurfaceClick(e.GetPosition(scene.View), e.ClickCount)) { e.Handled = true; return; }
        if (scene.HitButton(e.GetPosition(scene.View)) is { } button)
        {
            mouseButton = button; UpdateButtons(); CaptureMouse(); e.Handled = true; return;
        }
        if (e.ClickCount == 2) { scene.ResetView(); e.Handled = true; return; }
        ReleaseButtons();
        try { DragMove(); } catch (InvalidOperationException) { /* Mouse may have been released before native drag begins. */ }
        e.Handled = true;
    }
    internal bool HandleSurfaceClick(Point point, int clickCount)
    {
        if (IsViewTransitioning) return true;
        if (IsScreenCloseUp || scene.HitScreen(point))
        {
            if (clickCount == 2) ToggleScreenCloseUp();
            return true;
        }
        if (!scene.HitPowerSwitch(point)) return false;
        Close(); return true;
    }
    private void MouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!rotating) return;
        if (e.LeftButton != MouseButtonState.Pressed || (Keyboard.Modifiers & ModifierKeys.Control) == 0) { EndMouse(); return; }
        var current = e.GetPosition(this); scene.Rotate(current.X - lastMouse.X, current.Y - lastMouse.Y); lastMouse = current; e.Handled = true;
    }
    private void MouseUpHandler(object sender, MouseButtonEventArgs e) => EndMouse();
    private void EndMouse() { rotating = false; mouseButton = 0; UpdateButtons(); ReleaseMouseCapture(); Cursor = null; }
    private void ReleaseButtons() { heldKeys.Clear(); mouseButton = 0; UpdateButtons(); }
    private void UpdateButtons()
    {
        if (closed) return;
        GameBoyButton pressed = mouseButton;
        foreach (Key key in heldKeys) pressed |= MapKey(key);
        foreach (GameBoyButton button in Enum.GetValues<GameBoyButton>()) host.Input.SetButtonState(button, (pressed & button) != 0);
        scene.SetPressed(pressed);
    }
    internal static GameBoyButton MapKey(Key key) => key switch
    {
        Key.Up => GameBoyButton.Up, Key.Down => GameBoyButton.Down, Key.Left => GameBoyButton.Left, Key.Right => GameBoyButton.Right,
        Key.Z => GameBoyButton.A, Key.X => GameBoyButton.B, Key.Enter => GameBoyButton.Start, Key.Space => GameBoyButton.Select, _ => 0
    };
}
