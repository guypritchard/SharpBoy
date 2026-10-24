using System.Security.Cryptography;
using GB.Compiler;

namespace GB.Compiler.Tests;

// Sample-owned files, not compiler-generated artwork. Read fresh arrays for each caller.
internal static class SampleAssets
{
    public static string PathFor(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);
    public static byte[] BackgroundTiles => File.ReadAllBytes(PathFor("background.2bpp"));
    public static byte[] BackgroundMap => File.ReadAllBytes(PathFor("background.tilemap"));
    public static byte[] SpriteTiles => File.ReadAllBytes(PathFor("sprite.2bpp"));
    public static string BackgroundHash => Hash([.. BackgroundTiles, .. BackgroundMap]);
    public static string SpriteHash => Hash(SpriteTiles);
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static GameAssetBundle Background => new(BackgroundTiles, BackgroundMap);
    public static GameAssetBundle Sprite => new(spriteTiles: SpriteTiles);
    public static GameAssetBundle All => new(BackgroundTiles, BackgroundMap, SpriteTiles);
}
