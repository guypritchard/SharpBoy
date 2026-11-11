using System.Security.Cryptography;
using GB.Emulator.Hosting;

namespace GB.Emulator.AI.Tetris;

/// <summary>Read-only adapter verified against the private World ROM identified below.</summary>
public sealed class DmgTetrisObserver : ITetrisObserver
{
    public const string SupportedSha256 = "7FDE11DD4E594A6905DECCD57943D2909ECB37665A030741C42155AEB346323B";
    public static bool Supports(ReadOnlySpan<byte> rom) => Convert.ToHexString(SHA256.HashData(rom)) == SupportedSha256;

    // Origins are the ROM's active sprite pivot. Index +1 corresponds to button A
    // (the game's raw orientation number decreases). Geometry measured from OAM.
    private static readonly Cell[][][] Pieces =
    [
        [
            [new(-1,0), new(0,0), new(1,0), new(-1,1)],
            [new(-1,-1), new(0,-1), new(0,0), new(0,1)],
            [new(1,-1), new(-1,0), new(0,0), new(1,0)],
            [new(0,-1), new(0,0), new(0,1), new(1,1)]
        ],
        [
            [new(-1,0), new(0,0), new(1,0), new(1,1)],
            [new(0,-1), new(0,0), new(-1,1), new(0,1)],
            [new(-1,-1), new(-1,0), new(0,0), new(1,0)],
            [new(0,-1), new(1,-1), new(0,0), new(0,1)]
        ],
        [
            [new(-1,0), new(0,0), new(1,0), new(2,0)],
            [new(0,-2), new(0,-1), new(0,0), new(0,1)],
            [new(-1,0), new(0,0), new(1,0), new(2,0)],
            [new(0,-2), new(0,-1), new(0,0), new(0,1)]
        ],
        [
            [new(0,0), new(1,0), new(0,1), new(1,1)],
            [new(0,0), new(1,0), new(0,1), new(1,1)],
            [new(0,0), new(1,0), new(0,1), new(1,1)],
            [new(0,0), new(1,0), new(0,1), new(1,1)]
        ],
        [
            [new(-1,0), new(0,0), new(0,1), new(1,1)],
            [new(0,-1), new(-1,0), new(0,0), new(-1,1)],
            [new(-1,0), new(0,0), new(0,1), new(1,1)],
            [new(0,-1), new(-1,0), new(0,0), new(-1,1)]
        ],
        [
            [new(0,0), new(1,0), new(-1,1), new(0,1)],
            [new(-1,-1), new(-1,0), new(0,0), new(0,1)],
            [new(0,0), new(1,0), new(-1,1), new(0,1)],
            [new(-1,-1), new(-1,0), new(0,0), new(0,1)]
        ],
        [
            [new(-1,0), new(0,0), new(1,0), new(0,1)],
            [new(0,-1), new(-1,0), new(0,0), new(0,1)],
            [new(0,-1), new(-1,0), new(0,0), new(1,0)],
            [new(0,-1), new(0,0), new(1,0), new(0,1)]
        ]
    ];

    public TetrisObservation? Observe(EmulationInspection inspection)
    {
        if (inspection.CaptureHardware().StartingUp) return null;
        var high = inspection.ReadMemory(0xFF80, 0x7F);
        byte Read(int address) => high[address - 0xFF80];
        if (Read(0xFFE1) is not (0 or 0x1A) || Read(0xFFAB) != 0 || Read(0xFFE4) != 0 || Read(0xFF98) != 0) return null;
        var active = inspection.ReadMemory(0xC200, 4);
        if (active[0] != 0 || active[3] >= 28) return null;
        // 18 visible rows plus two empty rows above the LCD, preserving native pivots.
        var cells = inspection.ReadMemory(0xC802, 17 * 32 + 10);
        var board = new bool[200];
        for (int y = 0; y < 18; y++)
            for (int x = 0; x < 10; x++) board[(y + 2) * 10 + x] = cells[y * 32 + x] != 47;
        int xOrigin = (active[2] - 31) / 8, yOrigin = (active[1] - 16) / 8 + 2;
        byte preview = inspection.ReadMemory(0xC213, 1)[0];
        return new(board, Pieces[active[3] / 4], xOrigin, yOrigin, (4 - (active[3] & 3)) & 3,
            preview < 28 ? Pieces[preview / 4] : null, NextRotation: (4 - (preview & 3)) & 3);
    }
}
