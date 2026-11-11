using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using FlowDirection = System.Windows.FlowDirection;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

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
    private readonly GeometryModel3D bodyShadow;
    private readonly double cameraDistance;
    private readonly PerspectiveCamera camera;
    private double workspaceWidth, workspaceHeight;
    private Rect renderCrop;
    private readonly MatrixCamera croppedCamera = new();
    internal Rect RenderCrop => renderCrop;
    private static readonly int[] Palette = [unchecked((int)0xFFADB975), unchecked((int)0xFF829454), unchecked((int)0xFF435437), unchecked((int)0xFF1E3026)];
    public Viewport3D View { get; } = new() { ClipToBounds = false };
    public long FrameNumber { get; private set; } = -1;
    internal long PresentedFrames { get; private set; }
    internal long DeferredPresentations { get; private set; }
    internal BitmapSource ScreenImage => lcd;
    public Rect3D ScreenBounds { get; }
    public CartridgeAppearance? Cartridge { get; private set; }
    public double Yaw => yaw.Angle;
    public double Pitch => pitch.Angle;
    public int TriangleCount { get; }
    private LinkCableModel? cable;
    private ModelVisual3D? cableVisual;
    private readonly TranslateTransform3D bodyMovement = new();
    private Vector3D hubOffset = new(35, 5, 0);
    private readonly HashSet<GeometryModel3D> cableGeometry = new();
    private readonly HashSet<GeometryModel3D> hubGeometry = new();
    internal Vector3D ConsoleOffset => new(bodyMovement.OffsetX, bodyMovement.OffsetY, bodyMovement.OffsetZ);
    internal Point3D HubPosition => LinkCableModel.HubCenter + hubOffset;
    internal Point3D ConsoleSocket => body.Transform.Transform(LinkCableModel.PortAnchor);
    internal bool IsLinkVisible { get; private set; }
    internal LinkCableModel? Cable => cable;
    internal void SetLinkVisible(bool visible)
    {
        if (IsLinkVisible == visible) return;
        IsLinkVisible = visible;
        if (visible)
        {
            cable ??= new LinkCableModel(body.Transform, hubOffset); cableVisual ??= new() { Content = cable.Model }; View.Children.Add(cableVisual);
            void Add(Model3D part) { if (part is GeometryModel3D mesh) cableGeometry.Add(mesh); else if (part is Model3DGroup group) foreach (var child in group.Children) Add(child); }
            Add(cable.Model);
            void AddHub(Model3D part) { if (part is GeometryModel3D mesh) hubGeometry.Add(mesh); else if (part is Model3DGroup group) foreach (var child in group.Children) AddHub(child); }
            AddHub(cable.HubAssembly);
            AdvanceCable(0);
        }
        else
        {
            if (cableVisual != null) View.Children.Remove(cableVisual);
            bodyMovement.OffsetX = bodyMovement.OffsetY = bodyMovement.OffsetZ = 0;
            hubOffset = new(35, 5, 0); cable = null; cableVisual = null; cableGeometry.Clear(); hubGeometry.Clear();
        }
        camera.Position = new(visible ? 54 : 0, 0, cameraDistance);
        camera.FieldOfView = visible ? Math.Atan(Math.Tan(Math.PI/12)*1000/640)*360/Math.PI : 30;
        if (!visible) RestoreFullViewport();
    }
    internal bool HitHub(Point point) => HitSurface(point, hubGeometry.Contains);
    internal bool HitBody(Point point) => HitSurface(point, mesh => names.ContainsKey(mesh));
    internal void SetWorkspaceViewport(double width, double height, double scale)
    {
        workspaceWidth = width; workspaceHeight = height;
        camera.FieldOfView = Math.Atan(Math.Tan(Math.PI / 12) * width / (640 * scale)) * 360 / Math.PI;
        renderCrop = Rect.Empty; UpdateRenderCrop();
    }
    internal void AdvanceCable(double seconds)
    {
        if (IsLinkVisible) { cable!.Update(seconds, body.Transform, hubOffset); UpdateRenderCrop(); }
    }
    private void RestoreFullViewport()
    {
        renderCrop = Rect.Empty; View.Camera = camera; View.Margin = new Thickness(0);
        View.HorizontalAlignment = HorizontalAlignment.Stretch; View.VerticalAlignment = VerticalAlignment.Stretch;
        View.Width = double.NaN; View.Height = double.NaN;
    }
    private void UpdateRenderCrop()
    {
        if (!IsLinkVisible || workspaceWidth <= 0 || workspaceHeight <= 0) return;
        var bounds = Rect.Empty;
        var localBody = Rect3D.Empty;
        foreach (var part in body.Children) localBody.Union(part.Bounds);
        Include(localBody, body.Transform.Value);
        var localPlug = Rect3D.Empty;
        foreach (var part in cable!.ConsolePlug.Children) localPlug.Union(part.Bounds);
        Include(localPlug, cable.ConsolePlug.Transform.Value); Include(cable.HubAssembly.Bounds, Matrix3D.Identity);
        foreach (var point in cable.Simulation.Points) bounds.Union(ProjectWorkspace(point));
        if (bounds.IsEmpty) return;
        var required = bounds; required.Inflate(20, 20);
        var workspace = new Rect(0, 0, workspaceWidth, workspaceHeight);
        required.Intersect(workspace);
        if (!renderCrop.IsEmpty && renderCrop.Contains(required) &&
            renderCrop.Width < required.Width + 128 && renderCrop.Height < required.Height + 128) return;
        bounds.Inflate(40, 40); bounds.Intersect(workspace);
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return;
        double left = Math.Floor(bounds.Left / 8) * 8, top = Math.Floor(bounds.Top / 8) * 8;
        double right = Math.Min(workspaceWidth, Math.Ceiling(bounds.Right / 8) * 8), bottom = Math.Min(workspaceHeight, Math.Ceiling(bounds.Bottom / 8) * 8);
        renderCrop = new Rect(left, top, right - left, bottom - top);
        View.Width = renderCrop.Width; View.Height = renderCrop.Height;
        View.Margin = new Thickness(left, top, 0, 0);
        View.HorizontalAlignment = HorizontalAlignment.Left; View.VerticalAlignment = VerticalAlignment.Top;
        // Crop the original frustum with an off-centre projection. Moving the camera
        // instead would introduce parallax and make objects jump when the bounds change.
        double scaleX = 1 / Math.Tan(camera.FieldOfView * Math.PI / 360);
        double near = camera.NearPlaneDistance, far = camera.FarPlaneDistance;
        double offsetX = (workspaceWidth - 2 * left) / renderCrop.Width - 1;
        double offsetY = (2 * top + renderCrop.Height - workspaceHeight) / renderCrop.Height;
        croppedCamera.ViewMatrix = new Matrix3D(1,0,0,0, 0,1,0,0, 0,0,1,0, -camera.Position.X,-camera.Position.Y,-cameraDistance,1);
        croppedCamera.ProjectionMatrix = new Matrix3D(
            scaleX * workspaceWidth / renderCrop.Width,0,0,0,
            0,scaleX * workspaceWidth / renderCrop.Height,0,0,
            -offsetX,-offsetY,far / (near - far),-1,
            0,0,near * far / (near - far),0);
        View.Camera = croppedCamera;

        void Include(Rect3D box, Matrix3D transform)
        {
            if (box.IsEmpty) return;
            for (int x = 0; x <= 1; x++) for (int y = 0; y <= 1; y++) for (int z = 0; z <= 1; z++)
                bounds.Union(ProjectWorkspace(transform.Transform(new Point3D(box.X + x * box.SizeX, box.Y + y * box.SizeY, box.Z + z * box.SizeZ))));
        }
    }
    internal Vector3D ViewDeltaToWorld(Vector delta)
    {
        double width = IsLinkVisible && workspaceWidth > 0 ? workspaceWidth : View.ActualWidth;
        double unit = 2 * cameraDistance * Math.Tan(camera.FieldOfView * Math.PI / 360) / Math.Max(1, width);
        return new(delta.X * unit, -delta.Y * unit, 0);
    }
    internal void MoveLinkedObject(bool hub, Vector3D delta)
    {
        if (!IsLinkVisible || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y) || !double.IsFinite(delta.Z)) return;
        // Keep the actual bodies reachable inside the transparent desktop workspace.
        var center = hub ? HubPosition : (Point3D)ConsoleOffset;
        var screen = ProjectWorkspace(center + delta);
        var projectedCenter = ProjectWorld(center);
        var projectedCorner = ProjectWorld(center + (hub ? new Vector3D(35, 51, 0) : new Vector3D(56, 84, 0)));
        var margin = new Vector(Math.Abs(projectedCorner.X - projectedCenter.X) + 12, Math.Abs(projectedCorner.Y - projectedCenter.Y) + 12);
        double width = workspaceWidth > 0 ? workspaceWidth : View.ActualWidth, height = workspaceHeight > 0 ? workspaceHeight : View.ActualHeight;
        double x = Math.Clamp(screen.X, Math.Min(margin.X, width / 2), Math.Max(width / 2, width - margin.X));
        double y = Math.Clamp(screen.Y, Math.Min(margin.Y, height / 2), Math.Max(height / 2, height - margin.Y));
        delta += ViewDeltaToWorld(new Vector(x - screen.X, y - screen.Y));
        delta = LimitCableMovement(hub, delta);
        if (hub) hubOffset += delta;
        else { bodyMovement.OffsetX += delta.X; bodyMovement.OffsetY += delta.Y; bodyMovement.OffsetZ += delta.Z; }
        AdvanceCable(0);
    }
    private Vector3D LimitCableMovement(bool hub, Vector3D delta)
    {
        double spacing = cable!.Simulation.Spacing;
        var a = body.Transform.Transform(LinkCableModel.ConsoleTail + new Vector3D(spacing, 0, 0));
        var b = LinkCableModel.HubTail + hubOffset - new Vector3D(spacing, 0, 0);
        var fixedEnd = hub ? a : b; var movingEnd = hub ? b : a;
        var separation = movingEnd + delta - fixedEnd;
        var aToB = hub ? separation : -separation;
        if (aToB.LengthSquared > .000001) aToB.Normalize();
        var consoleOut = body.Transform.Transform(new Vector3D(1, 0, 0));
        // Reserve cord for the exit bends, especially when rotation puts the socket on
        // the far side of the shell. Straight-line distance alone can stretch a wrapped cord.
        double bends = 20 * (1 - Vector3D.DotProduct(consoleOut, aToB)) + 20 * (1 - aToB.X);
        double maximum = cable.Simulation.MaximumInnerSpan - bends;
        // Drag in the desktop plane: reaching the cord limit must not pull a model towards the camera.
        double planarLimit = Math.Sqrt(Math.Max(0, maximum * maximum - separation.Z * separation.Z));
        double planarDistance = Math.Sqrt(separation.X * separation.X + separation.Y * separation.Y);
        if (planarDistance > planarLimit && planarDistance > .00001)
        {
            separation.X *= planarLimit / planarDistance; separation.Y *= planarLimit / planarDistance;
            delta = fixedEnd + separation - movingEnd;
        }
        return delta;
    }
    internal bool HitLink(Point point)
    {
        bool hit = false;
        VisualTreeHelper.HitTest(View, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult decorative && IsDecorative(decorative.ModelHit)) return HitTestResultBehavior.Continue;
            if (result is RayMeshGeometry3DHitTestResult mesh && mesh.ModelHit is GeometryModel3D geometry)
                hit = cableGeometry.Contains(geometry) || names.TryGetValue(geometry, out var name) && name.StartsWith("dvi", StringComparison.Ordinal);
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(point));
        return hit;
    }

    public DesktopScene(double cameraDistance = 305)
    {
        if (!double.IsFinite(cameraDistance) || cameraDistance < 250) throw new ArgumentOutOfRangeException(nameof(cameraDistance));
        this.cameraDistance = cameraDistance;
        model = DmgModel.Load(Path.Combine(AppContext.BaseDirectory, "Desktop", "Assets", "Dmg.glb"));
        TriangleCount = model.Parts.Sum(p => ((MeshGeometry3D)p.Model.Geometry).TriangleIndices.Count / 3);
        camera = new PerspectiveCamera(new Point3D(0, 0, cameraDistance), new Vector3D(0, 0, -cameraDistance), new Vector3D(0, 1, 0), 30)
            { NearPlaneDistance = 10, FarPlaneDistance = 1000 };
        View.Camera = camera;
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
        transforms.Children.Add(bodyMovement);
        body.Transform = transforms;
        bodyShadow = DesktopShadow.Create(-59, -96, 126, 183, -25); body.Children.Insert(0, bodyShadow);
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
    { TryPresent(frame); }
    internal bool TryPresent(VideoFrame frame)
    {
        // WritePixels takes an unbounded compositor lock. Never let a busy software
        // renderer park the machine/audio owner while it waits to upload a texture.
        if (!lcd.TryLock(new Duration(TimeSpan.Zero))) { DeferredPresentations++; return false; }
        try
        {
            var source = frame.Pixels;
            for (int i = 0; i < lcdPixels.Length; i++) lcdPixels[i] = Palette[source[i]];
            if (lcd.BackBufferStride == 160 * 4) Marshal.Copy(lcdPixels, 0, lcd.BackBuffer, lcdPixels.Length);
            else for (int y = 0; y < 144; y++) Marshal.Copy(lcdPixels, y * 160, IntPtr.Add(lcd.BackBuffer, y * lcd.BackBufferStride), 160);
            lcd.AddDirtyRect(new Int32Rect(0, 0, 160, 144));
            FrameNumber = frame.Number; PresentedFrames++; return true;
        }
        finally { lcd.Unlock(); }
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
        if (IsLinkVisible)
        {
            var correction = LimitCableMovement(false, new());
            bodyMovement.OffsetX += correction.X; bodyMovement.OffsetY += correction.Y; bodyMovement.OffsetZ += correction.Z;
            AdvanceCable(0);
        }
    }
    public void ResetView() => SetView(-14, -5);
    internal Point Project(Point3D local)
    {
        return ProjectWorld(body.Transform.Transform(local));
    }
    internal Point ProjectWorld(Point3D world)
    {
        var projected = ProjectWorkspace(world);
        return renderCrop.IsEmpty ? projected : new(projected.X - renderCrop.X, projected.Y - renderCrop.Y);
    }
    private Point ProjectWorkspace(Point3D world)
    {
        double width = IsLinkVisible && workspaceWidth > 0 ? workspaceWidth : View.ActualWidth;
        double height = IsLinkVisible && workspaceHeight > 0 ? workspaceHeight : View.ActualHeight;
        double factor = width / (2 * Math.Tan(camera.FieldOfView * Math.PI / 360)) / (camera.Position.Z - world.Z);
        return new Point(width/2 + (world.X - camera.Position.X) * factor, height/2 - (world.Y - camera.Position.Y) * factor);
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
            if (hit is RayMeshGeometry3DHitTestResult decorative && IsDecorative(decorative.ModelHit)) return HitTestResultBehavior.Continue;
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
            if (hit is RayMeshGeometry3DHitTestResult decorative && IsDecorative(decorative.ModelHit)) return HitTestResultBehavior.Continue;
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
            if (hit is RayMeshGeometry3DHitTestResult decorative && IsDecorative(decorative.ModelHit)) return HitTestResultBehavior.Continue;
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
            if (hit is RayMeshGeometry3DHitTestResult decorative && IsDecorative(decorative.ModelHit)) return HitTestResultBehavior.Continue;
            if (hit is RayMeshGeometry3DHitTestResult mesh)
                result = matches((GeometryModel3D)mesh.ModelHit);
            return HitTestResultBehavior.Stop;
        }, new PointHitTestParameters(point));
        return result;
    }
    private bool IsDecorative(Model3D model) => ReferenceEquals(model, bodyShadow) || ReferenceEquals(model, cable?.Shadow);
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
