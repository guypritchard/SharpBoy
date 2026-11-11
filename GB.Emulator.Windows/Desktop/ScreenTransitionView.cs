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

    private readonly Point3DCollection[] buffers = [new([new(), new(), new(), new()]), new([new(), new(), new(), new()])];
    private int bufferIndex;

    internal void Update(Point[] modelCorners, double width, double height, double screenWidth, double screenHeight, double progress)
    {
        camera.Width = width;
        var positions = buffers[bufferIndex ^= 1];
        double left = (width - screenWidth) / 2, top = (height - screenHeight) / 2;
        for (int i = 0; i < 4; i++)
        {
            double flatX = left + (i is 1 or 2 ? screenWidth : 0);
            double flatY = top + (i >= 2 ? screenHeight : 0);
            double x = modelCorners[i].X * (1 - progress) + flatX * progress;
            double y = modelCorners[i].Y * (1 - progress) + flatY * progress;
            positions[i] = new(x - width / 2, height / 2 - y, 0);
        }
        quad.Positions = positions;
    }
}
