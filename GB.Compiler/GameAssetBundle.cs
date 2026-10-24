using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace GB.Compiler;

/// <summary>Immutable build-supplied DMG art. Null denotes an absent input; byte arrays are copied on construction.</summary>
public sealed class GameAssetBundle(byte[]? backgroundTiles = null, byte[]? backgroundMap = null, byte[]? spriteTiles = null)
{
    public ImmutableArray<byte> BackgroundTiles { get; } = backgroundTiles is null ? default : ImmutableArray.CreateRange(backgroundTiles);
    public ImmutableArray<byte> BackgroundMap { get; } = backgroundMap is null ? default : ImmutableArray.CreateRange(backgroundMap);
    public ImmutableArray<byte> SpriteTiles { get; } = spriteTiles is null ? default : ImmutableArray.CreateRange(spriteTiles);

    public IReadOnlyList<string> Validate(MemoryLayout layout)
    {
        var issues = new List<string>();
        if (BackgroundTiles.IsDefault != BackgroundMap.IsDefault)
            issues.Add("GBC009: --background-tiles and --background-map must be supplied together.");
        if (layout.UsesBackground && (BackgroundTiles.IsDefault || BackgroundMap.IsDefault))
            issues.Add("GBC009: GameDisplay.InitializeBackground requires --background-tiles and --background-map (or both GameAssetBundle inputs).");
        if (!layout.UsesBackground && (!BackgroundTiles.IsDefault || !BackgroundMap.IsDefault))
            issues.Add("GBC009: --background-tiles and --background-map are unused without GameDisplay.InitializeBackground.");
        if (layout.UsesSpriteInit && SpriteTiles.IsDefault)
            issues.Add("GBC009: GameSprites.Initialize requires --sprite-tiles (or GameAssetBundle sprite input).");
        if (!layout.UsesSpriteInit && !SpriteTiles.IsDefault)
            issues.Add("GBC009: --sprite-tiles is unused without GameSprites.Initialize.");
        if (!BackgroundTiles.IsDefault && BackgroundTiles.Length != 64)
            issues.Add($"GBC009: --background-tiles requires exactly 64 bytes (four 8x8 DMG 2bpp tiles); got {BackgroundTiles.Length}.");
        if (!BackgroundMap.IsDefault)
        {
            if (BackgroundMap.Length != 1024)
                issues.Add($"GBC009: --background-map requires exactly 1024 bytes (32x32 tile IDs); got {BackgroundMap.Length}.");
            var invalid = BackgroundMap.AsSpan().IndexOfAnyInRange((byte)4, byte.MaxValue);
            if (invalid >= 0)
                issues.Add($"GBC009: --background-map entry {invalid} references tile {BackgroundMap[invalid]}; available IDs are 0..3.");
        }
        if (!SpriteTiles.IsDefault && SpriteTiles.Length != 16)
            issues.Add($"GBC009: --sprite-tiles requires exactly 16 bytes (one 8x8 DMG 2bpp tile for tile ID 4); got {SpriteTiles.Length}.");
        return issues;
    }

    public int BankOneBytes => (BackgroundTiles.IsDefault ? 0 : BackgroundTiles.Length) +
        (BackgroundMap.IsDefault ? 0 : BackgroundMap.Length) + (SpriteTiles.IsDefault ? 0 : SpriteTiles.Length);
    public int SpriteAddress => 0x4000 + (BackgroundTiles.IsDefault ? 0 : BackgroundTiles.Length + BackgroundMap.Length);

    internal string Assembly()
    {
        var result = new StringBuilder();
        if (!BackgroundTiles.IsDefault)
        {
            result.AppendLine("SECTION \"GB background assets\", ROMX[$4000], BANK[1]");
            Emit(result, "gb_bg_tiles", BackgroundTiles);
            Emit(result, "gb_bg_map", BackgroundMap);
            result.AppendLine("gb_bg_end::");
        }
        if (!SpriteTiles.IsDefault)
        {
            result.AppendLine($"SECTION \"GB sprite tile\", ROMX[${SpriteAddress:X4}], BANK[1]");
            Emit(result, "gb_sprite_tile", SpriteTiles);
            result.AppendLine("gb_sprite_end::");
        }
        return result.ToString();
    }

    private static void Emit(StringBuilder result, string symbol, ImmutableArray<byte> data)
    {
        result.AppendLine(symbol + "::");
        foreach (var chunk in data.Chunk(16))
            result.AppendLine("    db " + string.Join(", ", chunk.Select(b => $"${b:X2}")));
    }

    internal string Report()
    {
        var result = new StringBuilder();
        void Line(string name, int address, ImmutableArray<byte> data)
        {
            if (!data.IsDefault)
                result.AppendLine($"display asset {name} bank 1 ${address:X4} size {data.Length} SHA-256 {Convert.ToHexString(SHA256.HashData(data.AsSpan())).ToLowerInvariant()}");
        }
        Line("background tiles", 0x4000, BackgroundTiles);
        Line("background map", 0x4000 + (BackgroundTiles.IsDefault ? 0 : BackgroundTiles.Length), BackgroundMap);
        Line("sprite tiles", SpriteAddress, SpriteTiles);
        return result.ToString();
    }
}
