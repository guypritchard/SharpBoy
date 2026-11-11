using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace GB.Emulator.Windows.Desktop;

/// <summary>One cached soft texture instead of a per-frame shader over the entire desktop viewport.</summary>
internal static class DesktopShadow
{
    private static BitmapSource? texture;
    internal static GeometryModel3D Create(double x, double y, double width, double height, double z)
    {
        if (texture == null)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                for (int i = 15; i >= 0; i--)
                {
                    double inset = 14 + i;
                    drawing.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(5, 0, 0, 0)), null,
                        new Rect(inset, inset, 256 - 2 * inset, 384 - 2 * inset), 16, 16);
                }
            var bitmap = new RenderTargetBitmap(256, 384, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); texture = bitmap;
        }
        var material = new DiffuseMaterial(new ImageBrush(texture)); material.Freeze();
        var mesh = new MeshGeometry3D
        {
            Positions = new([new(x,y,z),new(x+width,y,z),new(x+width,y+height,z),new(x,y+height,z)]),
            TextureCoordinates = new([new(0,1),new(1,1),new(1,0),new(0,0)]), TriangleIndices = new([0,1,2,0,2,3])
        };
        mesh.Freeze(); var model = new GeometryModel3D(mesh, material); model.Freeze(); return model;
    }
}
