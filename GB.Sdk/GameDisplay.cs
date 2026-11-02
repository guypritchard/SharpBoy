namespace GB.Sdk;

/// <summary>Polling-only DMG background display. These declarations execute only in compiled ROMs.</summary>
public static class GameDisplay
{
    /// <summary>Load caller-supplied background assets with the LCD safely disabled; enable BG last.</summary>
    /// <remarks>The build must supply 16..4096 bytes (1..256 whole DMG 2bpp tiles) and a 1024-byte map with IDs within the supplied tile bank
    /// through GameAssetBundle or --background-tiles and --background-map. There is no default artwork.</remarks>
    [GameIntrinsic]
    public static void InitializeBackground() => throw new PlatformNotSupportedException("Game Boy target only.");

    /// <summary>Wait until the NEXT VBlank begins, even when already inside VBlank.</summary>
    [GameIntrinsic]
    public static void WaitForNextVBlank() => throw new PlatformNotSupportedException("Game Boy target only.");

    /// <summary>Set background horizontal and vertical scroll registers, in that order. Does not require build assets.</summary>
    [GameIntrinsic]
    public static void SetScroll(byte x, byte y) => throw new PlatformNotSupportedException("Game Boy target only.");
}
