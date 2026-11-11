using System.Security.Cryptography;
using System.Text.Json;
using GB.Emulator.Hosting;

namespace GB.Emulator.AI.Tetris;

/// <summary>Explicit per-ROM adapter. No guessed Nintendo RAM addresses are built in.</summary>
public sealed class TetrisMemoryProfile : ITetrisObserver
{
    public string RomSha256 { get; init; } = "";
    public int BoardAddress { get; init; }
    public int BoardStride { get; init; } = 10;
    public byte EmptyCell { get; init; }
    public int XAddress { get; init; }
    public int YAddress { get; init; }
    public int PieceAddress { get; init; }
    public int RotationAddress { get; init; }
    public int StateAddress { get; init; }
    public byte PlayingState { get; init; }
    public int XOffset { get; init; }
    public int YOffset { get; init; }
    public Dictionary<byte, Cell[][]> Pieces { get; init; } = new();

    public static TetrisMemoryProfile Load(string path, ReadOnlySpan<byte> rom)
    {
        var profile = JsonSerializer.Deserialize<TetrisMemoryProfile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Tetris profile.");
        if (!string.Equals(profile.RomSha256, Convert.ToHexString(SHA256.HashData(rom)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This board-reader profile does not match the ROM SHA-256.");
        if (profile.BoardStride is < 10 or > 32 || !Ram(profile.BoardAddress, 19 * profile.BoardStride + 10) ||
            new[] { profile.XAddress, profile.YAddress, profile.PieceAddress, profile.RotationAddress, profile.StateAddress }.Any(a => !Ram(a, 1)) ||
            profile.XOffset is < -32 or > 32 || profile.YOffset is < -32 or > 32 || profile.Pieces == null || profile.Pieces.Count == 0 ||
            profile.Pieces.Values.Any(rs => rs is null || rs.Length is < 1 or > 4 || rs.Any(r => r is null || r.Length != 4 || r.Distinct().Count() != 4 || r.Any(c => c.X is < -4 or > 4 || c.Y is < -4 or > 4))))
            throw new InvalidDataException("Invalid Tetris RAM profile. Expected WRAM/HRAM addresses and four-cell rotations.");
        return profile;
    }

    private static bool Ram(int address, int length) => (address >= 0xC000 && address + length <= 0xE000) || (address >= 0xFF80 && address + length <= 0xFFFF);

    public TetrisObservation? Observe(EmulationInspection inspection)
    {
        byte Read(int address) => inspection.ReadMemory((ushort)address, 1)[0];
        if (inspection.CaptureHardware().StartingUp || Read(StateAddress) != PlayingState || !Pieces.TryGetValue(Read(PieceAddress), out var rotations)) return null;
        int rotation = Read(RotationAddress);
        if (rotation >= rotations.Length) return null;
        var memory = inspection.ReadMemory((ushort)BoardAddress, 19 * BoardStride + 10);
        var board = new bool[200];
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 10; x++) board[y * 10 + x] = memory[y * BoardStride + x] != EmptyCell;
        return new(board, rotations, Read(XAddress) + XOffset, Read(YAddress) + YOffset, rotation);
    }
}
