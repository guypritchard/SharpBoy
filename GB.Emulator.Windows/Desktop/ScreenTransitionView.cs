using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace GB.Emulator.Windows.Desktop;

/// <summary>A live LCD quad that unfolds from the projected model into a flat screen.</summary>
internal sealed class ScreenTransitionView : Viewport3D
{
    private readonly MeshGeometry3D quad = new()
    {
        TextureCoordinates = new PointCollection([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]),
        TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2])
    };
    private readonly OrthographicCamera camera = new(new(0, 0, 1000), new(0, 0, -1), new(0, 1, 0), 640);

    internal ScreenTransitionView(BitmapSource screen)
    {
        Camera = camera; IsHitTestVisible = false; Visibility = Visibility.Collapsed;
        var image = new ImageBrush(screen) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(Brushes.Black));
        material.Children.Add(new EmissiveMaterial(image));
        Children.Add(new ModelVisual3D { Content = new GeometryModel3D(quad, material) { BackMaterial = material } });
    }

    internal void Update(Point[] modelCorners, double width, double height, double modelHeight, double progress)
    {
        camera.Width = width;
        Point[] flat = [new(0, 0), new(width, 0), new(width, height), new(0, height)];
        var positions = new Point3DCollection();
        for (int i = 0; i < 4; i++)
        {
            double x = modelCorners[i].X * (1 - progress) + flat[i].X * progress;
            double y = (modelCorners[i].Y + (height - modelHeight) / 2) * (1 - progress) + flat[i].Y * progress;
            positions.Add(new(x - width / 2, height / 2 - y, 0));
        }
        quad.Positions = positions;
    }
}
