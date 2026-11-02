namespace GB.Sdk;

/// <summary>Polling-only 8x8 DMG OBJ API. Coordinates are raw OAM bytes: visible x = oamX - 8, y = oamY - 16.</summary>
public static class GameSprites
{
    /// <summary>Load the caller-supplied tile as tile 4, hide all 40 sprites, set both OBJ palettes, and enable OBJ without otherwise changing LCDC.</summary>
    /// <remarks>The build must supply exactly 16 bytes of DMG 2bpp sprite data through GameAssetBundle
    /// or --sprite-tiles. There is no default artwork. Set/Hide alone do not require this asset.</remarks>
    [GameIntrinsic]
    public static void Initialize() => throw new PlatformNotSupportedException("Game Boy target only.");

    /// <summary>Set OAM entry 0..39 to raw coordinates, fixed tile 4, and DMG attributes (bits 7..4 only). Index and attributes must be constants.</summary>
    [GameIntrinsic]
    public static void Set(byte index, byte oamX, byte oamY, byte attributes) => throw new PlatformNotSupportedException("Game Boy target only.");

    /// <summary>Hide OAM entry 0..39 by setting its raw Y byte to zero. Index must be constant.</summary>
    [GameIntrinsic]
    public static void Hide(byte index) => throw new PlatformNotSupportedException("Game Boy target only.");
}
