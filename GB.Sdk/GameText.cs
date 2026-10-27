namespace GB.Sdk;

/// <summary>Native text drawing using an ASCII-indexed background tile font.</summary>
public static class GameText
{
    /// <summary>Writes a compile-time printable ASCII literal (at most 32 characters) to a tile-map address.
    /// Writes outside the $9800..$9BFF background map are ignored in their entirety.
    /// Call with the LCD disabled or during a safe VRAM interval.
    /// No managed strings or allocations exist on the target.</summary>
    [GameIntrinsic]
    public static void Write(ushort address, string text) =>
        throw new PlatformNotSupportedException("GameText.Write requires the Game Boy compiler backend.");
}
