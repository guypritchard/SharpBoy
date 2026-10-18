namespace GB.Sdk;

/// <summary>Copies declared MBC1 ROM assets into the compiler-owned WRAM staging buffer.
/// This is not a managed array, VRAM transfer, RAM banking or function banking.</summary>
public static class GameBanking
{
    /// <summary>Physical ROM bank 2..N-1, CPU ROMX source $4000-$7FFF, $D000-relative buffer offset,
    /// byte length. Each argument must be a compile-time constant; source span must lie in one declared asset.</summary>
    [GameIntrinsic]
    public static void CopyToBuffer(byte bank, ushort sourceAddress, ushort bufferOffset, ushort length) =>
        throw new PlatformNotSupportedException("GameBanking.CopyToBuffer requires the MBC1 asset compiler profile.");

    /// <summary>Reads one byte at $D000 + offset, in the reserved 2048-byte staging buffer.</summary>
    [GameIntrinsic]
    public static byte ReadBufferByte(ushort offset) =>
        throw new PlatformNotSupportedException("GameBanking.ReadBufferByte requires the MBC1 asset compiler profile.");
}
