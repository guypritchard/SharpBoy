using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace GB.Emulator.Windows.Desktop;

internal sealed record DmgPart(string Name, GeometryModel3D Model);

/// <summary>Loads the bundled, attributed static glTF 2 model. No plugins, scripts or external URLs.</summary>
internal sealed class DmgModel
{
    public List<DmgPart> Parts { get; } = [];
    public BitmapSource CartridgeTexture { get; private set; } = null!;
    public string Credit { get; private set; } = "";

    public static DmgModel Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length is < 28 or > 32_000_000 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x46546C67 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)) != data.Length)
            throw new InvalidDataException("The bundled DMG model is not a valid glTF 2 binary.");
        int jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)));
        if (jsonLength < 2 || jsonLength > data.Length - 28 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)) != 0x4E4F534A)
            throw new InvalidDataException("Missing model JSON chunk.");
        int binaryStart = checked(28 + jsonLength);
        if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24 + jsonLength)) != 0x004E4942 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20 + jsonLength)) != data.Length - binaryStart)
            throw new InvalidDataException("Missing model binary chunk.");
        using var document = JsonDocument.Parse(data.AsMemory(20, jsonLength));
        JsonElement root = document.RootElement;
        var loaded = new DmgModel { Credit = root.GetProperty("asset").GetProperty("extras").GetProperty("author").GetString()! };
        JsonElement accessors = root.GetProperty("accessors"), views = root.GetProperty("bufferViews");
        ReadOnlyMemory<byte> View(int index)
        {
            var view = views[index];
            if (view.GetProperty("buffer").GetInt32() != 0) throw new InvalidDataException("External model buffers are unsupported.");
            int offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            int length = view.GetProperty("byteLength").GetInt32();
            if (offset < 0 || length < 0 || (long)binaryStart + offset + length > data.Length)
                throw new InvalidDataException("Model buffer is out of bounds.");
            return data.AsMemory(binaryStart + offset, length);
        }
        float[] Floats(int index, int width)
        {
            var accessor = accessors[index];
            if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.TryGetProperty("sparse", out _) ||
                accessor.GetProperty("type").GetString() != (width == 3 ? "VEC3" : "VEC2"))
                throw new InvalidDataException("Unsupported model vertex format.");
            int viewIndex = accessor.GetProperty("bufferView").GetInt32();
            var bytes = View(viewIndex).Span;
            int offset = accessor.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            int stride = views[viewIndex].TryGetProperty("byteStride", out var s) ? s.GetInt32() : width * 4;
            int count = accessor.GetProperty("count").GetInt32();
            if (count is < 0 or > 100_000 || offset < 0 || stride < width * 4 ||
                (long)offset + Math.Max(0, count - 1) * stride + width * 4 > bytes.Length)
                throw new InvalidDataException("Model vertices exceed their buffer.");
            var values = new float[count * width];
            for (int v = 0; v < count; v++)
                for (int c = 0; c < width; c++)
                {
                    float value = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + v * stride + c * 4, 4));
                    if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite model vertex.");
                    values[v * width + c] = value;
                }
            return values;
        }
        Int32Collection Indices(int index, int vertexCount)
        {
            var accessor = accessors[index];
            var bytes = View(accessor.GetProperty("bufferView").GetInt32()).Span;
            int offset = accessor.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            int width = accessor.GetProperty("componentType").GetInt32() switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 };
            int count = accessor.GetProperty("count").GetInt32();
            if (width == 0 || accessor.GetProperty("type").GetString() != "SCALAR" || count is < 0 or > 300_000 ||
                count % 3 != 0 || offset < 0 || (long)offset + count * width > bytes.Length)
                throw new InvalidDataException("Invalid model triangle indices.");
            var result = new Int32Collection(count);
            for (int i = 0; i < count; i++)
            {
                var at = bytes.Slice(offset + i * width, width);
                uint value = width switch { 1 => at[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(at), _ => BinaryPrimitives.ReadUInt32LittleEndian(at) };
                if (value >= vertexCount) throw new InvalidDataException("Triangle references a missing vertex.");
                result.Add((int)value);
            }
            return result;
        }
        var textures = new Dictionary<int, BitmapSource>();
        BitmapSource Texture(int index)
        {
            int imageIndex = root.GetProperty("textures")[index].GetProperty("source").GetInt32();
            if (textures.TryGetValue(imageIndex, out var cached)) return cached;
            var image = root.GetProperty("images")[imageIndex];
            using var stream = new MemoryStream(View(image.GetProperty("bufferView").GetInt32()).ToArray(), false);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 2048; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            textures.Add(imageIndex, bitmap); return bitmap;
        }
        var materials = root.GetProperty("materials").EnumerateArray().Select(material =>
        {
            var pbr = material.GetProperty("pbrMetallicRoughness");
            System.Windows.Media.Brush brush = pbr.TryGetProperty("baseColorTexture", out var map)
                ? new ImageBrush(Texture(map.GetProperty("index").GetInt32())) { ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute, Viewport = new System.Windows.Rect(0, 0, 1, 1), TileMode = TileMode.Tile }
                : new SolidColorBrush(Color.FromRgb(18, 20, 18));
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(brush));
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(38, 36, 33)), 38));
            group.Freeze();
            return group;
        }).ToArray();
        loaded.CartridgeTexture = Texture(root.GetProperty("materials")[3].GetProperty("pbrMetallicRoughness")
            .GetProperty("baseColorTexture").GetProperty("index").GetInt32());
        var nodes = root.GetProperty("nodes");
        var visited = new HashSet<int>();
        void Visit(int index, Matrix4x4 parent)
        {
            if (!visited.Add(index)) throw new InvalidDataException("Model node hierarchy is cyclic or shared.");
            var node = nodes[index];
            Matrix4x4 local;
            if (node.TryGetProperty("matrix", out var matrix))
            {
                float[] a = matrix.EnumerateArray().Select(n => n.GetSingle()).ToArray();
                if (a.Length != 16) throw new InvalidDataException("Invalid model transform.");
                local = new(a[0],a[1],a[2],a[3], a[4],a[5],a[6],a[7], a[8],a[9],a[10],a[11], a[12],a[13],a[14],a[15]);
            }
            else
            {
                float[] Values(string name, float[] fallback) => node.TryGetProperty(name, out var value)
                    ? value.EnumerateArray().Select(v => v.GetSingle()).ToArray() : fallback;
                var t = Values("translation", [0,0,0]); var r = Values("rotation", [0,0,0,1]); var s = Values("scale", [1,1,1]);
                local = Matrix4x4.CreateScale(s[0],s[1],s[2]) * Matrix4x4.CreateFromQuaternion(new(r[0],r[1],r[2],r[3])) * Matrix4x4.CreateTranslation(t[0],t[1],t[2]);
            }
            var world = local * parent;
            if (!Matrix4x4.Invert(world, out var inverse)) throw new InvalidDataException("Singular model transform.");
            var normalMatrix = Matrix4x4.Transpose(inverse);
            if (node.TryGetProperty("mesh", out var meshIndex))
                foreach (var primitive in root.GetProperty("meshes")[meshIndex.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if (primitive.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
                        throw new InvalidDataException("Only static model triangles are supported.");
                    var attributes = primitive.GetProperty("attributes");
                    var positions = Floats(attributes.GetProperty("POSITION").GetInt32(), 3);
                    var normals = Floats(attributes.GetProperty("NORMAL").GetInt32(), 3);
                    var uv = Floats(attributes.GetProperty("TEXCOORD_0").GetInt32(), 2);
                    if (positions.Length != normals.Length || positions.Length / 3 != uv.Length / 2)
                        throw new InvalidDataException("Mismatched model vertex attributes.");
                    var geometry = new MeshGeometry3D();
                    for (int i = 0; i < positions.Length / 3; i++)
                    {
                        var point = Vector3.Transform(new(positions[i*3],positions[i*3+1],positions[i*3+2]), world) * 1000;
                        var normal = Vector3.Normalize(Vector3.TransformNormal(new(normals[i*3],normals[i*3+1],normals[i*3+2]), normalMatrix));
                        geometry.Positions.Add(new(point.X,point.Y,point.Z));
                        geometry.Normals.Add(new(normal.X,normal.Y,normal.Z));
                        geometry.TextureCoordinates.Add(new Point(uv[i*2],uv[i*2+1]));
                    }
                    geometry.TriangleIndices = Indices(primitive.GetProperty("indices").GetInt32(), positions.Length / 3);
                    geometry.Freeze();
                    loaded.Parts.Add(new(node.GetProperty("name").GetString()!, new(geometry, materials[primitive.GetProperty("material").GetInt32()])));
                }
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray()) Visit(child.GetInt32(), world);
        }
        foreach (var node in root.GetProperty("scenes")[root.GetProperty("scene").GetInt32()].GetProperty("nodes").EnumerateArray())
            Visit(node.GetInt32(), Matrix4x4.Identity);
        return loaded;
    }
}
