using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using FlowDirection = System.Windows.FlowDirection;

namespace GB.Emulator.Windows.Desktop;

/// <summary>Original DMG-style plugs, a simulated cord and a pixel LCD link monitor.</summary>
internal sealed class LinkCableModel
{
    internal static readonly Point3D PortAnchor = new(44.2, 40.5, -5.1);
    internal static readonly Point3D HubCenter = new(127, -10, 0);
    internal static readonly Point3D HubSocket = new(98.5, -10, 0);
    internal static readonly Point3D ConsoleTail = PortAnchor + new Vector3D(32, 0, 0);
    internal static readonly Point3D HubTail = HubSocket - new Vector3D(32, 0, 0);
    internal static readonly Rect3D HubCollisionBounds = new(95, -57, -12, 64, 94, 25);
    internal Model3DGroup Model { get; } = new();
    internal Model3DGroup Hub { get; } = new();
    internal Model3DGroup HubAssembly { get; } = new();
    internal Model3DGroup ConsolePlug { get; }
    internal CableSimulation Simulation { get; } = new();
    internal LinkActivityDisplay Activity { get; } = new();
    private readonly GeometryModel3D wire;
    private readonly MeshGeometry3D wireMesh = new();
    private readonly TranslateTransform3D hubMovement = new();
    private double meshElapsed;
    private Matrix3D lastConsoleMatrix;
    private readonly Point3D[] smoothPoints = new Point3D[97];
    private const int WireSides = 10;
    private static readonly (double Cos, double Sin)[] WireRing = Enumerable.Range(0, WireSides)
        .Select(i => (Math.Cos(i * Math.PI * 2 / WireSides), Math.Sin(i * Math.PI * 2 / WireSides))).ToArray();
    private readonly Point3DCollection[] wirePositions = [new(), new()];
    private readonly Vector3DCollection[] wireNormals = [new(), new()];
    private int wireBuffer;
    internal long MeshUpdates { get; private set; }
    internal Rect3D WireBounds => wire.Bounds;
    internal GeometryModel3D Shadow { get; }
    internal Point3D Start { get; private set; }
    internal Point3D End { get; private set; }
    private readonly SolidColorBrush led = new(Color.FromRgb(70, 79, 78));
    internal int TriangleCount { get; }

    internal LinkCableModel(Transform3D? initialConsolePose = null, Vector3D? initialHubOffset = null)
    {
        var rubber = Material(Color.FromRgb(28, 31, 32), 42, 55);
        var grip = Material(Color.FromRgb(115, 118, 112), 38, 25);
        var edge = Material(Color.FromRgb(61, 65, 64), 30, 40);
        var metal = Material(Color.FromRgb(158, 165, 166), 135, 100);
        var gold = Material(Color.FromRgb(189, 150, 70), 90, 55);
        var shell = Material(Color.FromRgb(189, 189, 172), 65, 38);
        // The actual imported model's EXT socket is on +X at y=40.5, z=-5.1.
        ConsolePlug = Plug(PortAnchor, 1, grip, edge, metal, gold, rubber);
        Model.Children.Add(ConsolePlug);
        HubAssembly.Children.Add(Plug(HubSocket, -1, grip, edge, metal, gold, rubber));
        HubAssembly.Children.Add(Hub); HubAssembly.Transform = hubMovement;
        wire = new(wireMesh, rubber) { BackMaterial = rubber }; Model.Children.Add(wire);
        // A small DMG-inspired serial instrument: cream shell, grey screen surround,
        // four-shade 160x144 LCD, purple/blue trim and a dedicated connection lamp.
        // The DMG silhouette has modest top corners and a much larger lower-right sweep.
        Hub.Children.Add(RoundedBox(new(127, -10, -1.1), 58, 88, 15, 3.5, 15, .8, edge));
        Hub.Children.Add(RoundedBox(new(127, -9.5, 1), 57.3, 87, 13.5, 3.3, 14.6, .75, shell));
        Hub.Children.Add(RoundedBox(new(127, -10, -6.3), 56, 86, 2, 3, 14, .4, grip));
        Shadow = DesktopShadow.Create(89, -66, 83, 119, -13.5); Hub.Children.Insert(0, Shadow);
        Hub.Children.Add(Box(new(127, 29, 7.9), 49, .4, .15, .04, grip));
        Hub.Children.Add(Print(new(109, 30.3, 7.9), 25, 1.8, "SERIAL LINK", Color.FromRgb(112,114,105)));
        // A real side socket beneath the inserted connector, rather than a plug floating by the housing.
        Hub.Children.Add(Box(new(98.1, -10, 0), 1.2, 13, 8, .5, edge));
        Hub.Children.Add(Box(new(97.5, -10, 0), .6, 10.8, 5.8, .2, metal));
        var bezel = Material(Color.FromRgb(87, 88, 98), 48, 45);
        var face = new Model3DGroup { Transform = new TranslateTransform3D(0, 9, 0) }; Hub.Children.Add(face);
        face.Children.Add(RoundedBox(new(127.8, -8, 8.1), 51, 46, 1.7, 3.2, 8.5, .25, bezel));
        face.Children.Add(RoundedBox(new(130.2, -8, 9.05), 39.3, 35.5, .55, 2.5, 2.5, .1, rubber));
        var lcdBrush = new ImageBrush(Activity.Image) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(lcdBrush, BitmapScalingMode.NearestNeighbor);
        var lcdMaterial = new MaterialGroup(); lcdMaterial.Children.Add(new DiffuseMaterial(System.Windows.Media.Brushes.Black));
        lcdMaterial.Children.Add(new EmissiveMaterial(lcdBrush));
        face.Children.Add(RoundedQuad(new(111.2, -25.1, 9.4), 38, 34.2, 2.2, lcdMaterial));
        var reflection = new LinearGradientBrush(Color.FromArgb(26,225,239,230), Color.FromArgb(0,225,239,230), new Point(0,0), new Point(.8,1));
        var glass = new MaterialGroup(); glass.Children.Add(new EmissiveMaterial(reflection));
        glass.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(35,220,230,220)), 100));
        face.Children.Add(RoundedQuad(new(111.2, -25.1, 9.46), 38, 34.2, 2.2, glass));
        var purple = Material(Color.FromRgb(111, 39, 88), 25, 30); var blue = Material(Color.FromRgb(42, 53, 111), 25, 30);
        face.Children.Add(Box(new(108, 12.9, 9.04), 8, .65, .12, .05, purple));
        face.Children.Add(Box(new(108, 11.7, 9.04), 8, .45, .12, .04, blue));
        face.Children.Add(Box(new(148.5, 12.9, 9.04), 5, .65, .12, .05, purple));
        face.Children.Add(Box(new(148.5, 11.7, 9.04), 5, .45, .12, .04, blue));
        face.Children.Add(Print(new(114, 11.2, 9.06), 30, 2.1, "DOT MATRIX LINK DISPLAY", Color.FromRgb(203,205,195)));
        face.Children.Add(Print(new(102, -4, 9.06), 7.5, 2.3, "LINK", Color.FromRgb(210,212,202)));
        Hub.Children.Add(Print(new(104, -29, 7.85), 29, 5, "LINK BOY", Color.FromRgb(40,51,112), italic: true));
        Hub.Children.Add(Print(new(104, -32, 7.85), 29, 2, "SERIAL ACTIVITY MONITOR", Color.FromRgb(83,85,84)));
        for (int i = 0; i < 5; i++)
        {
            var vent = Box(new(140 + i * 2.4, -43, 7.9), .8, 7.5, .35, .2, edge);
            vent.Transform = new RotateTransform3D(new AxisAngleRotation3D(new(0,0,1), -20), new Point3D(140 + i * 2.4, -43, 7.9));
            Hub.Children.Add(vent);
        }
        foreach (var point in new[] { new Point3D(102, -39, -8), new Point3D(152, -39, -8), new Point3D(102, 18, -8), new Point3D(152, 18, -8) })
        {
            Hub.Children.Add(Disc(point, 1.1, metal));
            Hub.Children.Add(Box(point + new Vector3D(0, 0, .1), 1.2, .2, .1, .05, edge));
        }
        var indicator = new MaterialGroup(); indicator.Children.Add(new DiffuseMaterial(led)); indicator.Children.Add(new EmissiveMaterial(led));
        face.Children.Add(Disc(new(105.5, 2.5, 9.1), 2.0, rubber));
        face.Children.Add(Disc(new(105.5, 2.5, 9.2), 1.35, indicator));
        face.Children.Add(Disc(new(105.1, 3.0, 9.25), .35, Material(Color.FromRgb(237,181,165), 20, 40)));
        Model.Children.Add(HubAssembly);
        FreezeDetails(Model);
        Update(0, initialConsolePose ?? Transform3D.Identity, initialHubOffset ?? new Vector3D(35, 5, 0));
        TriangleCount = Count(Model);
    }
    internal void Update(double seconds, Transform3D consoleTransform, Vector3D hubOffset)
    {
        var matrix = consoleTransform.Value;
        var nextStart = consoleTransform.Transform(ConsoleTail); var nextEnd = HubTail + hubOffset;
        bool moved = Start != nextStart || End != nextEnd;
        if (!moved && matrix == lastConsoleMatrix && Simulation.IsSleeping) return;
        lastConsoleMatrix = matrix;
        ConsolePlug.Transform = consoleTransform;
        hubMovement.OffsetX = hubOffset.X; hubMovement.OffsetY = hubOffset.Y; hubMovement.OffsetZ = hubOffset.Z;
        Start = nextStart; End = nextEnd;
        AdvanceSimulation(seconds, matrix, hubOffset, consoleTransform.Transform(new Vector3D(1, 0, 0)));
        if (!moved && Simulation.IsSleeping && wireMesh.Positions.Count != 0) return;
        meshElapsed += Math.Max(0, seconds);
        if (!moved && meshElapsed < 1d / 30 && wireMesh.Positions.Count != 0) return;
        meshElapsed = 0;
        Smooth(Simulation.Points, smoothPoints, 2); UpdateWire(smoothPoints);
    }
    private void AdvanceSimulation(double seconds, Matrix3D matrix, Vector3D hubOffset, Vector3D consoleOut)
    {
        var inverse = matrix; inverse.Invert();
        Point3D Exclude(Point3D p)
        {
            var local = inverse.Transform(p);
            local = CableSimulation.OutsideBox(local, new(-47, -77, -19, 94, 154, 38));
            p = matrix.Transform(local);
            return CableSimulation.OutsideBox(p - hubOffset, HubCollisionBounds) + hubOffset;
        }
        Simulation.Advance(seconds, Start, consoleOut, End, new(-1,0,0), Exclude);
    }
    private void UpdateWire(Point3D[] points)
    {
        const int sides = WireSides;
        bool buildIndices = wireMesh.TriangleIndices.Count == 0;
        if (buildIndices)
            for (int buffer = 0; buffer < 2; buffer++)
                for (int i = 0; i < points.Length * sides; i++)
                { wirePositions[buffer].Add(new()); wireNormals[buffer].Add(new()); }
        // Fill the buffer that is detached from the scene graph. Reusing these
        // collections avoids allocating and freezing a new vertex buffer every frame.
        wireBuffer = 1 - wireBuffer;
        var positions = wirePositions[wireBuffer]; var normals = wireNormals[wireBuffer];
        Vector3D normal = new(0, 0, 1);
        for (int i = 0; i < points.Length; i++)
        {
            var direction = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(0, i - 1)];
            if (direction.LengthSquared < .000001) direction = new(1, 0, 0);
            direction.Normalize();
            // Parallel-transport the ring frame to avoid visible flips when the cord swings in depth.
            normal -= direction * Vector3D.DotProduct(normal, direction);
            if (normal.LengthSquared < .000001) normal = Vector3D.CrossProduct(direction, Math.Abs(direction.Z) < .8 ? new(0,0,1) : new(0,1,0));
            normal.Normalize(); var binormal = Vector3D.CrossProduct(direction, normal);
            for (int side = 0; side < sides; side++)
            {
                var n = normal * WireRing[side].Cos + binormal * WireRing[side].Sin;
                positions[i * sides + side] = points[i] + n * 1.65; normals[i * sides + side] = n;
                if (!buildIndices || i == 0) continue;
                int a = (i-1)*sides+side, b = (i-1)*sides+(side+1)%sides, c = i*sides+side, d = i*sides+(side+1)%sides;
                foreach (int index in new[] { a,b,d,a,d,c }) wireMesh.TriangleIndices.Add(index);
            }
        }
        if (buildIndices) wireMesh.TriangleIndices.Freeze();
        wireMesh.Positions = positions; wireMesh.Normals = normals; MeshUpdates++;
    }
    private static void FreezeDetails(Model3D model)
    {
        if (model is Model3DGroup group) { foreach (var child in group.Children) FreezeDetails(child); }
        else if (model is GeometryModel3D geometry && geometry.Geometry.IsFrozen && geometry.Material.IsFrozen && geometry.BackMaterial?.IsFrozen != false)
            geometry.Freeze();
    }
    internal Color LedColor => led.Color;
    internal void SetStatus(LinkActivitySnapshot activity)
    {
        Activity.Update(activity);
        led.Color = activity.Online && activity.Connected ? Color.FromRgb(239,48,44) :
            activity.Online ? Color.FromRgb(255,180,48) : Color.FromRgb(68,39,39);
    }
    private static int Count(Model3D model) => model is Model3DGroup group ? group.Children.Sum(Count) :
        model is GeometryModel3D { Geometry: MeshGeometry3D mesh } ? mesh.TriangleIndices.Count / 3 : 0;
    private static Model3DGroup Plug(Point3D anchor, int direction, Material grip, Material edge, Material metal, Material gold, Material rubber)
    {
        var plug = new Model3DGroup();
        Point3D P(double x, double y = 0, double z = 0) => anchor + new Vector3D(x * direction, y, z);
        plug.Children.Add(Box(P(2), 6, 11, 6, .9, metal));
        plug.Children.Add(Box(P(1.5), 6.2, 7.2, 3, .45, rubber));
        for (int i = 0; i < 3; i++)
        {
            plug.Children.Add(Box(P(1, -3 + i * 3, 1.6), 4.5, .65, .25, .1, gold));
            plug.Children.Add(Box(P(1, -3 + i * 3, -1.6), 4.5, .65, .25, .1, gold));
        }
        plug.Children.Add(Box(P(11), 16, 15, 9, 1.7, grip));
        plug.Children.Add(Box(P(5), 1.1, 14, 8.8, .7, edge));
        for (int i = 0; i < 4; i++) plug.Children.Add(Box(P(9 + i * 2.2, 0, 4.5), .6, 9, .4, .15, edge));
        // Tapered, closely spaced ribs catch the side light like moulded PVC.
        plug.Children.Add(Tube([P(18), P(32)], 1.9, rubber, 18));
        for (int i = 0; i < 7; i++)
        {
            double radius = 3.3 - i * .22;
            plug.Children.Add(Tube([P(19 + i * 1.7), P(20 + i * 1.7)], radius, rubber, 18));
        }
        return plug;
    }
    internal static Material Material(Color color, byte shine = 70, double power = 60)
    {
        var material = new MaterialGroup(); material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(shine, shine, shine)), power));
        material.Freeze(); return material;
    }
    private static GeometryModel3D Geometry(MeshGeometry3D mesh, Material material)
    { mesh.Freeze(); return new(mesh, material) { BackMaterial = material }; }

    private static Point3D[] RoundedOutline(Point3D center, double width, double height, double radius, double lowerRightRadius)
    {
        const int arcSteps = 16;
        var points = new List<Point3D>(4 * (arcSteps + 1));
        double halfWidth = width / 2, halfHeight = height / 2;
        // Counter-clockwise, starting on the bottom edge. These are circular arcs,
        // not the straight 45-degree cuts used by connector chamfers.
        foreach (var (right, top, cornerRadius, startAngle) in new[]
        {
            (true, false, lowerRightRadius, -90d), (true, true, radius, 0d),
            (false, true, radius, 90d), (false, false, radius, 180d)
        })
        {
            double r = Math.Clamp(cornerRadius, .01, Math.Min(width, height) * .49);
            double x = center.X + (right ? halfWidth - r : -halfWidth + r);
            double y = center.Y + (top ? halfHeight - r : -halfHeight + r);
            for (int step = 0; step <= arcSteps; step++)
            {
                double angle = (startAngle + step * 90d / arcSteps) * Math.PI / 180;
                points.Add(new(x + Math.Cos(angle) * r, y + Math.Sin(angle) * r, center.Z));
            }
        }
        return points.ToArray();
    }

    private static GeometryModel3D RoundedBox(Point3D center, double width, double height, double depth,
        double radius, double lowerRightRadius, double bevel, Material material)
    {
        double b = Math.Min(bevel, depth * .45);
        Point3D[] Ring(double inset, double z) => RoundedOutline(new(center.X, center.Y, z),
            width - 2 * inset, height - 2 * inset, Math.Max(.01, radius - inset), Math.Max(.01, lowerRightRadius - inset));
        var rings = new[] { Ring(b, center.Z - depth / 2), Ring(0, center.Z - depth / 2 + b),
            Ring(0, center.Z + depth / 2 - b), Ring(b, center.Z + depth / 2) };
        var mesh = new MeshGeometry3D();
        void Triangle(Point3D a, Point3D c, Point3D d)
        {
            int at = mesh.Positions.Count;
            mesh.Positions.Add(a); mesh.Positions.Add(c); mesh.Positions.Add(d);
            var normal = Vector3D.CrossProduct(c - a, d - a); if (normal.LengthSquared > 0) normal.Normalize();
            mesh.Normals.Add(normal); mesh.Normals.Add(normal); mesh.Normals.Add(normal);
            mesh.TriangleIndices.Add(at); mesh.TriangleIndices.Add(at + 1); mesh.TriangleIndices.Add(at + 2);
        }
        int count = rings[0].Length;
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Triangle(rings[ring][i], rings[ring][j], rings[ring + 1][j]);
                Triangle(rings[ring][i], rings[ring + 1][j], rings[ring + 1][i]);
            }
        for (int i = 1; i < count - 1; i++)
        { Triangle(rings[3][0], rings[3][i], rings[3][i + 1]); Triangle(rings[0][0], rings[0][i + 1], rings[0][i]); }
        return Geometry(mesh, material);
    }

    private static GeometryModel3D RoundedQuad(Point3D bottomLeft, double width, double height, double radius, Material material)
    {
        var center = bottomLeft + new Vector3D(width / 2, height / 2, 0);
        var outline = RoundedOutline(center, width, height, radius, radius);
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(center); mesh.TextureCoordinates.Add(new(.5, .5)); mesh.Normals.Add(new(0,0,1));
        foreach (var p in outline)
        {
            mesh.Positions.Add(p); mesh.Normals.Add(new(0,0,1));
            // Preserve the complete LCD's UV mapping, clipping only the physical corner pixels.
            mesh.TextureCoordinates.Add(new((p.X - bottomLeft.X) / width, 1 - (p.Y - bottomLeft.Y) / height));
        }
        for (int i = 0; i < outline.Length; i++)
        { mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add((i + 1) % outline.Length + 1); }
        return Geometry(mesh, material);
    }

    internal static GeometryModel3D Box(Point3D center, double width, double height, double depth, double bevel, Material material)
    {
        var mesh = new MeshGeometry3D();
        double b = Math.Min(bevel, Math.Min(depth / 2, Math.Min(width, height) / 3));
        Point3D[] Ring(double inset, double z)
        {
            double w = width / 2 - inset, h = height / 2 - inset, c = Math.Max(.05, bevel - inset * .5);
            return [new(center.X-w+c,center.Y-h,z),new(center.X+w-c,center.Y-h,z),new(center.X+w,center.Y-h+c,z),new(center.X+w,center.Y+h-c,z),
                new(center.X+w-c,center.Y+h,z),new(center.X-w+c,center.Y+h,z),new(center.X-w,center.Y+h-c,z),new(center.X-w,center.Y-h+c,z)];
        }
        var rings = new[] { Ring(b * .4, center.Z - depth / 2), Ring(0, center.Z - depth / 2 + b * .4), Ring(0, center.Z + depth / 2 - b * .4), Ring(b * .4, center.Z + depth / 2) };
        void Triangle(Point3D a, Point3D c, Point3D d)
        {
            int at = mesh.Positions.Count; mesh.Positions.Add(a); mesh.Positions.Add(c); mesh.Positions.Add(d);
            var normal = Vector3D.CrossProduct(c - a, d - a); if (normal.LengthSquared > 0) normal.Normalize();
            mesh.Normals.Add(normal); mesh.Normals.Add(normal); mesh.Normals.Add(normal);
            mesh.TriangleIndices.Add(at); mesh.TriangleIndices.Add(at + 1); mesh.TriangleIndices.Add(at + 2);
        }
        for (int r = 0; r < 3; r++) for (int i = 0; i < 8; i++)
            { int j = (i + 1) % 8; Triangle(rings[r][i], rings[r][j], rings[r + 1][j]); Triangle(rings[r][i], rings[r + 1][j], rings[r + 1][i]); }
        for (int i = 1; i < 7; i++) { Triangle(rings[3][0], rings[3][i], rings[3][i + 1]); Triangle(rings[0][0], rings[0][i + 1], rings[0][i]); }
        return Geometry(mesh, material);
    }
    internal static GeometryModel3D Tube(IReadOnlyList<Point3D> points, double radius, Material material, int sides = 16)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < points.Count; i++)
        {
            Vector3D direction = points[Math.Min(i + 1, points.Count - 1)] - points[Math.Max(0, i - 1)]; direction.Normalize();
            Vector3D normal = Vector3D.CrossProduct(direction, Math.Abs(direction.Z) < .85 ? new(0, 0, 1) : new(0, 1, 0)); normal.Normalize();
            Vector3D binormal = Vector3D.CrossProduct(direction, normal);
            for (int side = 0; side < sides; side++)
            {
                double angle = side * Math.PI * 2 / sides; var n = normal * Math.Cos(angle) + binormal * Math.Sin(angle);
                mesh.Positions.Add(points[i] + n * radius); mesh.Normals.Add(n);
                if (i == 0) continue;
                int a = (i - 1) * sides + side, b = (i - 1) * sides + (side + 1) % sides, c = i * sides + side, d = i * sides + (side + 1) % sides;
                foreach (int index in new[] { a, b, d, a, d, c }) mesh.TriangleIndices.Add(index);
            }
        }
        return Geometry(mesh, material);
    }
    private static void Smooth(IReadOnlyList<Point3D> controls, Point3D[] points, int segments)
    {
        int at = 0;
        for (int i = 0; i < controls.Count - 1; i++) for (int step = 0; step < segments; step++)
            {
                double t = (double)step / segments;
                Vector3D a = (Vector3D)controls[Math.Max(0, i - 1)], b = (Vector3D)controls[i], c = (Vector3D)controls[i + 1], d = (Vector3D)controls[Math.Min(controls.Count - 1, i + 2)];
                points[at++] = (Point3D)(.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t));
            }
        points[at] = controls[^1];
    }
    private static GeometryModel3D Disc(Point3D center, double radius, Material material)
    {
        var mesh = new MeshGeometry3D(); mesh.Positions.Add(center); mesh.Normals.Add(new(0, 0, 1));
        for (int i = 0; i <= 32; i++) { double a = i * Math.PI * 2 / 32; mesh.Positions.Add(center + new Vector3D(Math.Cos(a) * radius, Math.Sin(a) * radius, 0)); mesh.Normals.Add(new(0, 0, 1)); }
        for (int i = 0; i < 32; i++) foreach (int n in new[] { 0, i + 1, i + 2 }) mesh.TriangleIndices.Add(n);
        return Geometry(mesh, material);
    }
    private static GeometryModel3D Print(Point3D bottomLeft, double width, double height, string text, Color ink, bool italic = false)
    {
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            var type = new Typeface(new System.Windows.Media.FontFamily("Arial"), italic ? FontStyles.Italic : FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, type, 48, new SolidColorBrush(ink), 1);
            drawing.PushTransform(new ScaleTransform(500 / Math.Max(1, formatted.Width), 60 / Math.Max(1, formatted.Height)));
            drawing.DrawText(formatted, new Point()); drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(512, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        var material = new DiffuseMaterial(new ImageBrush(bitmap)); material.Freeze();
        return Quad(bottomLeft, width, height, material);
    }
    private static GeometryModel3D Quad(Point3D bottomLeft, double width, double height, Material material)
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new([bottomLeft, bottomLeft + new Vector3D(width, 0, 0), bottomLeft + new Vector3D(width, height, 0), bottomLeft + new Vector3D(0, height, 0)]),
            TextureCoordinates = new([new(0, 1), new(1, 1), new(1, 0), new(0, 0)]),
            TriangleIndices = new([0, 1, 2, 0, 2, 3])
        };
        return Geometry(mesh, material);
    }
}
