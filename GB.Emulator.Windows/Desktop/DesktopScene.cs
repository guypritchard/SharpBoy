using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using FlowDirection = System.Windows.FlowDirection;

namespace GB.Emulator.Windows.Desktop;

internal sealed class DesktopScene : IVideoOutput
{
    private readonly DmgModel model;
    private readonly Model3DGroup body = new();
    private readonly Dictionary<GeometryModel3D, string> names = new();
    private readonly Dictionary<GeometryModel3D, TranslateTransform3D> presses = new();
    private readonly AxisAngleRotation3D yaw = new(new Vector3D(0, 1, 0), -14);
    private readonly AxisAngleRotation3D pitch = new(new Vector3D(1, 0, 0), -5);
    private readonly WriteableBitmap lcd = new(160, 144, 96, 96, PixelFormats.Bgra32, null);
    private readonly int[] lcdPixels = new int[160 * 144];
    private readonly LinearGradientBrush reflection;
    private readonly GeometryModel3D lcdSurface, glassSurface;
    private readonly double cameraDistance;
    private static readonly int[] Palette = [unchecked((int)0xFFADB975), unchecked((int)0xFF829454), unchecked((int)0xFF435437), unchecked((int)0xFF1E3026)];
    public Viewport3D View { get; } = new() { ClipToBounds = false };
    public long FrameNumber { get; private set; } = -1;
    internal BitmapSource ScreenImage => lcd;
    public Rect3D ScreenBounds { get; }
    public CartridgeAppearance? Cartridge { get; private set; }
    public double Yaw => yaw.Angle;
    public double Pitch => pitch.Angle;
    public int TriangleCount { get; }

    public DesktopScene(double cameraDistance = 305)
    {
        if (!double.IsFinite(cameraDistance) || cameraDistance < 250) throw new ArgumentOutOfRangeException(nameof(cameraDistance));
        this.cameraDistance = cameraDistance;
        model = DmgModel.Load(Path.Combine(AppContext.BaseDirectory, "Desktop", "Assets", "Dmg.glb"));
        TriangleCount = model.Parts.Sum(p => ((MeshGeometry3D)p.Model.Geometry).TriangleIndices.Count / 3);
        View.Camera = new PerspectiveCamera(new Point3D(0, 0, cameraDistance), new Vector3D(0, 0, -cameraDistance), new Vector3D(0, 1, 0), 30)
            { NearPlaneDistance = 10, FarPlaneDistance = 1000 };
        View.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 11, Direction = 285, Opacity = .38, Color = Colors.Black };
        var lighting = new Model3DGroup();
        lighting.Children.Add(new AmbientLight(Color.FromRgb(75, 80, 92)));
        lighting.Children.Add(new DirectionalLight(Color.FromRgb(181, 170, 150), new Vector3D(1, -1, -2)));
        lighting.Children.Add(new DirectionalLight(Color.FromRgb(61, 85, 114), new Vector3D(-1, -.3, -1)));
        lighting.Children.Add(new DirectionalLight(Color.FromRgb(95, 82, 66), new Vector3D(.4, 1, 2)));
        View.Children.Add(new ModelVisual3D { Content = lighting });
        foreach (var part in model.Parts)
        {
            // Keep the inserted cartridge. Discard the loose decorative cartridge and PCB.
            if (part.Name.Contains("low001")) continue;
            if (part.Name == "screen_low_External_0") { ScreenBounds = part.Model.Bounds; continue; }
            names[part.Model] = part.Name;
            body.Children.Add(part.Model);
            if (ButtonName(part.Name) != null || part.Name.StartsWith("movement_low"))
            {
                var movement = new TranslateTransform3D(); part.Model.Transform = movement; presses[part.Model] = movement;
            }
        }
        if (ScreenBounds.IsEmpty || ScreenBounds.SizeX < 40) throw new InvalidDataException("The DMG model has no usable LCD surface.");
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new RotateTransform3D(pitch)); transforms.Children.Add(new RotateTransform3D(yaw));
        body.Transform = transforms;
        View.Children.Add(new ModelVisual3D { Content = body });
        // The authored screen mesh includes the hidden area beneath the bezel.
        // Its visible aperture needs a 10% inset, verified by ray-testing every
        // video-border pixel at front and oblique viewing angles.
        double width = Math.Min(ScreenBounds.SizeX, ScreenBounds.SizeY * 160 / 144) * .90;
        double height = width * 144 / 160;
        double x = ScreenBounds.X + (ScreenBounds.SizeX - width) / 2;
        double y = ScreenBounds.Y + (ScreenBounds.SizeY - height) / 2;
        double z = ScreenBounds.Z + ScreenBounds.SizeZ + .12;
        var backing = new MaterialGroup();
        backing.Children.Add(new DiffuseMaterial(Brushes.Black));
        backing.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(173, 185, 117))));
        backing.Freeze();
        body.Children.Add(Plane(ScreenBounds.X, ScreenBounds.Y, ScreenBounds.SizeX, ScreenBounds.SizeY, z - .04, backing));
        var pixels = new ImageBrush(lcd) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(pixels, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(View, BitmapScalingMode.HighQuality);
        var lcdMaterial = new MaterialGroup();
        lcdMaterial.Children.Add(new DiffuseMaterial(Brushes.Black));
        lcdMaterial.Children.Add(new EmissiveMaterial(pixels));
        lcdSurface = Plane(x, y, width, height, z, lcdMaterial);
        body.Children.Add(lcdSurface);
        reflection = new LinearGradientBrush { StartPoint = new Point(-.1, 0), EndPoint = new Point(.95, 1) };
        reflection.GradientStops.Add(new GradientStop(Color.FromArgb(2, 216, 235, 255), 0));
        reflection.GradientStops.Add(new GradientStop(Color.FromArgb(25, 240, 246, 255), .20));
        reflection.GradientStops.Add(new GradientStop(Color.FromArgb(3, 240, 246, 255), .42));
        reflection.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
        var glass = new MaterialGroup(); glass.Children.Add(new EmissiveMaterial(reflection));
        glass.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(55, 235, 244, 255)), 105));
        glassSurface = Plane(x, y, width, height, z + .055, glass);
        body.Children.Add(glassSurface);
        ShowWelcome();
        // No decorative label is displayed before an actual ROM is loaded.
        SetCartridge(null);
    }

    public void SetCartridge(CartridgeAppearance? cartridge)
    {
        Cartridge = cartridge;
        BitmapSource? cartridgeTexture = cartridge?.CreateTexture(model.CartridgeTexture);
        foreach (var part in model.Parts.Where(p => p.Name is "cartridge_low_Cartridge_0" or "chip_low_Cartridge_0"))
        {
            if (cartridge == null) { body.Children.Remove(part.Model); continue; }
            if (!body.Children.Contains(part.Model)) body.Children.Insert(0, part.Model);
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new ImageBrush(cartridgeTexture) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0,0,1,1), TileMode = TileMode.Tile }));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(32, 32, 32)), 30));
            material.Freeze(); part.Model.Material = material;
        }
    }
    public void Present(VideoFrame frame)
    {
        var source = frame.Pixels;
        for (int i = 0; i < lcdPixels.Length; i++) lcdPixels[i] = Palette[source[i]];
        lcd.WritePixels(new Int32Rect(0, 0, 160, 144), lcdPixels, 160 * 4, 0);
        FrameNumber = frame.Number;
    }
    private void ShowWelcome()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(173, 185, 117)), null, new Rect(0, 0, 160, 144));
            void Text(string text, double size, double top)
            {
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), size, new SolidColorBrush(Color.FromRgb(40, 59, 39)), 1);
                context.DrawText(formatted, new Point((160 - formatted.Width)/2, top));
            }
            Text("SHARPBOY", 20, 33); Text("DMG-01", 10, 61); Text("CTRL+O  OPEN ROM", 9, 99);
        }
        var bitmap = new RenderTargetBitmap(160, 144, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        bitmap.CopyPixels(lcdPixels, 160 * 4, 0); lcd.WritePixels(new Int32Rect(0, 0, 160, 144), lcdPixels, 160 * 4, 0);
    }
    public void Rotate(double deltaX, double deltaY) => SetView(yaw.Angle + deltaX * .48, pitch.Angle + deltaY * .42);
    public void SetView(double y, double p)
    {
        if (!double.IsFinite(y) || !double.IsFinite(p)) throw new ArgumentOutOfRangeException(nameof(y), "Viewing angles must be finite.");
        yaw.Angle = Math.IEEERemainder(y, 360); pitch.Angle = Math.Clamp(p, -85, 85);
        reflection.StartPoint = new Point(-.1 + Math.Sin(yaw.Angle * Math.PI / 180) * .3, -.1 + pitch.Angle / 240);
    }
    public void ResetView() => SetView(-14, -5);
    internal Point Project(Point3D local)
    {
        Point3D world = body.Transform.Transform(local);
        double factor = View.ActualWidth / (2 * Math.Tan(15 * Math.PI / 180)) / (cameraDistance - world.Z);
        return new Point(View.ActualWidth/2 + world.X * factor, View.ActualHeight/2 - world.Y * factor);
    }
    internal Point[] ProjectScreenCorners()
    {
        var b = glassSurface.Bounds;
        return [Project(new(b.X, b.Y + b.SizeY, b.Z)), Project(new(b.X + b.SizeX, b.Y + b.SizeY, b.Z)),
            Project(new(b.X + b.SizeX, b.Y, b.Z)), Project(new(b.X, b.Y, b.Z))];
    }
    internal bool IsScreenPixelVisible(int x, int y)
    {
        var bounds = glassSurface.Bounds;
        var point = Project(new Point3D(bounds.X + (x + .5) * bounds.SizeX / 160,
            bounds.Y + (143.5 - y) * bounds.SizeY / 144, bounds.Z));
        bool visible = false;
        VisualTreeHelper.HitTest(View, null, hit =>
        {
            if (hit is RayMeshGeometry3DHitTestResult mesh)
                visible = ReferenceEquals(mesh.ModelHit, lcdSurface) || ReferenceEquals(mesh.ModelHit, glassSurface);
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(point));
        return visible;
    }
    public GameBoyButton? HitButton(Point point)
    {
        GameBoyButton? result = null;
        VisualTreeHelper.HitTest(View, null, hit =>
        {
            if (hit is RayMeshGeometry3DHitTestResult mesh && names.TryGetValue((GeometryModel3D)mesh.ModelHit, out var name))
            {
                Point3D local = body.Transform.Inverse!.Transform(mesh.PointHit);
                result = ButtonName(name);
                if (name.StartsWith("select_low")) result = local.X < -5 ? GameBoyButton.Select : GameBoyButton.Start;
                if (name.StartsWith("movement_low") || name.StartsWith("movementknobs"))
                {
                    double dx = local.X + 27, dy = local.Y + 27;
                    result = Math.Abs(dx) > Math.Abs(dy) ? dx < 0 ? GameBoyButton.Left : GameBoyButton.Right
                        : dy < 0 ? GameBoyButton.Down : GameBoyButton.Up;
                }
            }
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(point));
        return result;
    }
    internal bool HitScreen(Point point) => HitSurface(point, mesh =>
        ReferenceEquals(mesh, lcdSurface) || ReferenceEquals(mesh, glassSurface));

    internal bool HitPowerSwitch(Point point)
    {
        bool result = false;
        VisualTreeHelper.HitTest(View, null, hit =>
        {
            if (hit is RayMeshGeometry3DHitTestResult mesh && names.TryGetValue((GeometryModel3D)mesh.ModelHit, out var name))
            {
                var local = body.Transform.Inverse!.Transform(mesh.PointHit);
                // The small physical slider is on the top edge. Also accept its
                // OFF/ON marking on the front lip, visible in a straight-on view.
                result = name == "switch_low_External_0" ||
                    (name == "base_low_Main_0" && local.X is >= -37 and <= -16 &&
                     local.Y is >= 66 and <= 74 && local.Z > 10);
            }
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(point));
        return result;
    }

    private bool HitSurface(Point point, Func<GeometryModel3D, bool> matches)
    {
        bool result = false;
        VisualTreeHelper.HitTest(View, null, hit =>
        {
            if (hit is RayMeshGeometry3DHitTestResult mesh)
                result = matches((GeometryModel3D)mesh.ModelHit);
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(point));
        return result;
    }
    public void SetPressed(GameBoyButton buttons)
    {
        foreach (var pair in presses)
        {
            string name = names[pair.Key];
            GameBoyButton mask = ButtonName(name) ?? (GameBoyButton.Left | GameBoyButton.Right | GameBoyButton.Up | GameBoyButton.Down);
            pair.Value.OffsetZ = (buttons & mask) != 0 ? -.7 : 0;
        }
    }
    private static GameBoyButton? ButtonName(string name) => name switch
    {
        "a_low_External_0" => GameBoyButton.B, "b_low_External_0" => GameBoyButton.A,
        "select_low_External_0" => GameBoyButton.Select, _ => null
    };
    private static GeometryModel3D Plane(double x, double y, double width, double height, double z, Material material)
    {
        var geometry = new MeshGeometry3D
        {
            Positions = new Point3DCollection([new(x,y,z),new(x+width,y,z),new(x+width,y+height,z),new(x,y+height,z)]),
            TextureCoordinates = new PointCollection([new(0,1),new(1,1),new(1,0),new(0,0)]),
            TriangleIndices = new Int32Collection([0,1,2,0,2,3])
        };
        geometry.Freeze(); return new(geometry, material);
    }
}
