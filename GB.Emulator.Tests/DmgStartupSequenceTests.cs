#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Core.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class DmgStartupSequenceTests
{
    private static readonly byte[] ValidLogo =
    {
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
        0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
        0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC,
        0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E
    };

    [TestMethod]
    public void DecodesCartridgeLogoTilemapAndDisplaysScrollingPixels()
    {
        var gb = CreateGameboy();
        CpuStepResult first = gb.Step();
        Assert.AreEqual("DMG startup frame", first.Instruction.Name);
        Assert.AreEqual(0, first.Instruction.Length);
        Assert.IsTrue(first.ReadAddresses.Contains((ushort)0x0104));
        Assert.IsTrue(first.WrittenAddresses.Contains((ushort)0x8010));
        Assert.AreEqual(0xF0, gb.Memory.Read8(0x8010)); // top nibble C, horizontally doubled
        Assert.AreEqual(0xF0, gb.Memory.Read8(0x8012)); // vertically doubled
        Assert.AreEqual(0, gb.Memory.Read8(0x8011)); // second bitplane remains clear
        for (int column = 0; column < 12; column++)
        {
            Assert.AreEqual(column + 1, gb.Memory.Read8((ushort)(0x9904 + column)), $"upper tile {column}");
            Assert.AreEqual(column + 13, gb.Memory.Read8((ushort)(0x9924 + column)), $"lower tile {column}");
        }
        Assert.AreEqual(0xFC, gb.Memory.Read8(0xFF47));
        Assert.AreEqual(0x91, gb.Memory.Read8(0xFF40));
        Assert.AreEqual(100, gb.Memory.Read8(0xFF42));

        byte[] initial = Render(gb);
        Assert.AreEqual(0, initial[32]);
        for (int i = 1; i < 200; i++) gb.RunStep();
        byte[] landed = Render(gb);
        Assert.AreEqual(0, gb.Memory.Read8(0xFF42));
        Assert.AreNotEqual(initial[64 * 160 + 32], landed[64 * 160 + 32]);

        // Independent 48x8 raster from the annotated DMG startup logo rows.
        // No tile decoding, map lookup or nibble expansion is used as the oracle.
        string[] logoRows =
        {
            "XX.. .XX. XX.. .... .... .... .... .... .... ...X X... ....",
            "XXX. .XX. XX.. .... ..XX .... .... .... .... ...X X... ....",
            "XXX. .XX. .... .... .XXX X... .... .... .... ...X X... ....",
            "XX.X .XX. XX.X X.XX ..XX ..XX XX.. XX.X X... XXXX X..X XXX.",
            "XX.X .XX. XX.X XX.X X.XX .XX. .XX. XXX. XX.X X..X X.XX ..XX",
            "XX.. XXX. XX.X X..X X.XX .XXX XXX. XX.. XX.X X..X X.XX ..XX",
            "XX.. XXX. XX.X X..X X.XX .XX. .... XX.. XX.X X..X X.XX ..XX",
            "XX.. .XX. XX.X X..X X.XX ..XX XXX. XX.. XX.. XXXX X..X XXX."
        };
        Assert.HasCount(8, logoRows);
        for (int sourceY = 0; sourceY < logoRows.Length; sourceY++)
        {
            string row = logoRows[sourceY].Replace(" ", "");
            Assert.AreEqual(48, row.Length, $"source row {sourceY}");
            for (int sourceX = 0; sourceX < row.Length; sourceX++)
            {
                Assert.IsTrue(row[sourceX] is 'X' or '.', $"source ({sourceX},{sourceY})");
                int expectedShade = row[sourceX] == 'X' ? 3 : 0; // BGP=$FC, low bitplane only
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                        Assert.AreEqual(expectedShade, landed[(64 + sourceY * 2 + dy) * 160 + 32 + sourceX * 2 + dx],
                            $"logo pixel ({32 + sourceX * 2 + dx},{64 + sourceY * 2 + dy})");
            }
        }
    }

    [TestMethod]
    public void RegisteredSymbolUsesTileTwentyFiveBesideUpperLogoRow()
    {
        var gb = CreateGameboy();
        gb.RunStep();
        Assert.AreEqual(0x3C, gb.Memory.Read8(0x8190));
        Assert.AreEqual(0x3C, gb.Memory.Read8(0x819E));
        Assert.AreEqual(0, gb.Memory.Read8(0x81A0));
        Assert.AreEqual(0, gb.Memory.Read8(0x8310));
        Assert.AreEqual(0x19, gb.Memory.Read8(0x9910));
        for (int frame = 2; frame <= 200; frame++) gb.RunStep();
        Assert.AreEqual(3, Render(gb)[64 * 160 + 130]); // registered mark at x=128
    }

    [TestMethod]
    public void PacksAllCartridgeLogoBytesIntoTwentyFourDisplayedTiles()
    {
        byte[] rom = MakeRom();
        for (int i = 0; i < ValidLogo.Length; i++)
            rom[0x104 + i] = (byte)((((i * 7 + 1) & 15) << 4) | ((i * 11 + 2) & 15));
        var gb = new Gameboy(enableDmgStartup: true);
        gb.Load(new Cartridge { Data = rom });
        gb.RunStep();

        // Two source bytes fill each tile: four doubled rows per byte. In
        // particular, bytes 46/47 fill tile 24 at $8180-$818F.
        for (int i = 0; i < ValidLogo.Length; i++)
        {
            for (int half = 0; half < 2; half++)
            {
                int nibble = half == 0 ? rom[0x104 + i] >> 4 : rom[0x104 + i] & 15;
                byte expanded = ExpandNibble(nibble);
                int rowBase = 0x8010 + (i * 2 + half) * 4;
                Assert.AreEqual(expanded, gb.Memory.Read8((ushort)rowBase), $"byte {i}, half {half}");
                Assert.AreEqual(0, gb.Memory.Read8((ushort)(rowBase + 1)));
                Assert.AreEqual(expanded, gb.Memory.Read8((ushort)(rowBase + 2)));
                Assert.AreEqual(0, gb.Memory.Read8((ushort)(rowBase + 3)));
            }
        }
        for (int frame = 2; frame <= 200; frame++) gb.RunStep();
        byte[] screen = Render(gb);
        for (int tile = 0; tile < 24; tile++)
        {
            int tileX = 32 + (tile % 12) * 8;
            // Tile-map row $9904 displays 1..12 above row $9924's 13..24.
            int tileY = tile < 12 ? 64 : 72;
            for (int row = 0; row < 8; row++)
            {
                int source = tile * 2 + row / 4;
                int nibble = row % 4 < 2 ? rom[0x104 + source] >> 4 : rom[0x104 + source] & 15;
                byte pixels = ExpandNibble(nibble);
                for (int x = 0; x < 8; x++)
                    Assert.AreEqual((pixels >> (7 - x) & 1) * 3, screen[(tileY + row) * 160 + tileX + x],
                        $"tile {tile + 1}, row {row}, pixel {x}");
            }
        }
    }

    [TestMethod]
    public void ScrollsEveryTwoFramesThenHolds64FramesAndHandsOff()
    {
        var gb = CreateGameboy();
        var frameScroll = new List<byte>();
        var frameTone = new List<byte>();
        gb.Video.FrameReady += (_, _) =>
        {
            frameScroll.Add(gb.Memory.Read8(0xFF42));
            frameTone.Add(gb.Memory.Peek(0xFF13));
        };
        for (int i = 1; i <= 264; i++)
        {
            Assert.IsFalse(gb.RunStep());
            Assert.AreEqual(i, gb.StartupFramesCompleted);
            Assert.HasCount(i, frameScroll);
            Assert.AreEqual(i * 70224L, gb.EmulatedCycles);
            Assert.AreEqual(0x0100, Cpu.Registers.PC);
            Assert.AreEqual(i < 264, gb.IsStartupSequenceActive);
            Assert.AreEqual(100 - Math.Min(i / 2, 100), gb.Memory.Read8(0xFF42));
            Assert.AreEqual(100 - Math.Min(i / 2, 100), frameScroll[i - 1], $"emitted frame {i}");
        }
        Assert.AreEqual(100, frameScroll[0]);
        Assert.AreEqual(99, frameScroll[1]);
        Assert.AreEqual(0, frameScroll[199]);
        Assert.AreEqual(0, frameTone[194]);
        Assert.AreEqual(0x83, frameTone[195]);
        Assert.AreEqual(0x83, frameTone[198]);
        Assert.AreEqual(0xC1, frameTone[199]);
        Assert.AreEqual(0x01, Cpu.Registers.A);
        Assert.AreEqual(0xB0, Cpu.Registers.F);
        Assert.AreEqual(0x0013, Cpu.Registers.BC);
        Assert.AreEqual(0x00D8, Cpu.Registers.DE);
        Assert.AreEqual(0x014D, Cpu.Registers.HL);
        Assert.AreEqual(0xFFFE, Cpu.Registers.SP);
        Assert.AreEqual(0x91, gb.Memory.Read8(0xFF40));
        Assert.AreEqual(0, gb.Memory.Read8(0xFF43));
        Assert.AreEqual(0xFC, gb.Memory.Read8(0xFF47));
        Assert.AreEqual(0, gb.Memory.Read8(0xFFFF));
        Assert.AreEqual(1, gb.Memory.Read8(0xFF0F));
        gb.RunStep();
        Assert.AreEqual(0x0101, Cpu.Registers.PC);
    }

    [TestMethod]
    public void ChimeUsesExactPulsePeriodsAndProducesAudiblePcm()
    {
        var gb = CreateGameboy();
        int audibleBatches = 0;
        gb.Sound.SamplesReady += samples =>
        {
            foreach (short sample in samples.Span)
            {
                if (sample == 0) continue;
                audibleBatches++;
                break;
            }
        };
        for (int i = 0; i < 195; i++) gb.RunStep();
        Assert.AreEqual(0, gb.Memory.Peek(0xFF13));
        gb.RunStep(); // count $62, frame 196
        Assert.AreEqual(0x83, gb.Memory.Peek(0xFF13));
        Assert.AreEqual(0x87, gb.Memory.Peek(0xFF14));
        Assert.AreEqual(0xF3, gb.Memory.Peek(0xFF12));
        Assert.AreEqual(0xF3, gb.Memory.Peek(0xFF25));
        Assert.AreEqual(0x77, gb.Memory.Peek(0xFF24));
        int firstPeriod = (gb.Memory.Peek(0xFF14) & 7) * 256 + gb.Memory.Peek(0xFF13);
        Assert.AreEqual(0x783, firstPeriod);
        Assert.AreEqual(1048.576, 131072.0 / (2048 - firstPeriod), 0.001);
        gb.RunStep(); gb.RunStep(); gb.RunStep();
        Assert.AreEqual(0x83, gb.Memory.Peek(0xFF13));
        Assert.IsGreaterThan(0, audibleBatches);
        gb.RunStep(); // count $64, frame 200
        Assert.AreEqual(0xC1, gb.Memory.Peek(0xFF13));
        Assert.AreEqual(0x87, gb.Memory.Peek(0xFF14));
        int secondPeriod = (gb.Memory.Peek(0xFF14) & 7) * 256 + gb.Memory.Peek(0xFF13);
        Assert.AreEqual(0x7C1, secondPeriod);
        Assert.AreEqual(2080.508, 131072.0 / (2048 - secondPeriod), 0.001);
    }

    [TestMethod]
    public void BadAndShortLogosWarnButRenderActualBytesAndExecuteCartridge()
    {
        foreach (byte[] rom in new[] { MakeRom(), new byte[0x105], new byte[0x101] })
        {
            rom[0x100] = 0x00;
            if (rom.Length > 0x104) rom[0x104] = 0x80;
            var listener = new RecordingListener();
            Trace.Listeners.Add(listener);
            try
            {
                var gb = new Gameboy(enableDmgStartup: true);
                gb.Load(new Cartridge { Data = rom });
                gb.RunStep();
                Assert.IsTrue(listener.Messages.Any(s => s.Contains("DMG startup") &&
                    (s.Contains("mismatch") || s.Contains("short"))));
                if (rom.Length > 0x104)
                {
                    Assert.AreEqual(0xC0, gb.Memory.Read8(0x8010)); // supplied 8 nibble, not expected C
                    if (rom.Length == 0x105) Assert.AreEqual(0, gb.Memory.Read8(0x8018)); // missing next byte
                    for (int i = 1; i < 200; i++) gb.RunStep();
                    byte[] screen = Render(gb);
                    Assert.AreEqual(3, screen[64 * 160 + 32]);
                    Assert.AreEqual(0, screen[64 * 160 + 34]);
                }
                while (gb.IsStartupSequenceActive) gb.RunStep();
                gb.RunStep();
                Assert.AreEqual(0x0101, Cpu.Registers.PC);
            }
            finally { Trace.Listeners.Remove(listener); }
        }
    }

    [TestMethod]
    public void LoadResetAndMidAnimationRestoreReplayIdenticalFrames()
    {
        var gb = CreateGameboy();
        for (int i = 0; i < 197; i++) gb.RunStep();
        GameboyState saved = gb.CaptureState();
        CpuStepResult first = gb.Step();
        byte[] screen = Render(gb);
        long cycles = gb.EmulatedCycles;
        int progress = gb.StartupFramesCompleted;
        gb.RestoreState(saved);
        CpuStepResult replay = gb.Step();
        Assert.AreEqual(progress, gb.StartupFramesCompleted);
        Assert.AreEqual(cycles, gb.EmulatedCycles);
        Assert.AreEqual(first.Disassembly, replay.Disassembly);
        CollectionAssert.AreEquivalent(first.WrittenAddresses.ToArray(), replay.WrittenAddresses.ToArray());
        CollectionAssert.AreEqual(screen, Render(gb));
        gb.Load(new Cartridge { Data = MakeRom(validLogo: true) });
        Assert.IsTrue(gb.IsStartupSequenceActive);
        Assert.AreEqual(0, gb.StartupFramesCompleted);
        Assert.AreEqual(0L, gb.EmulatedCycles);
        gb.Step();
        Assert.AreEqual(100, gb.Memory.Read8(0xFF42));
    }

    [TestMethod]
    public void DefaultGameboyNeverStartsAnimation()
    {
        var gb = new Gameboy();
        gb.Load(new Cartridge { Data = MakeRom(validLogo: true) });
        Assert.IsFalse(gb.IsStartupSequenceActive);
        gb.RunStep();
        Assert.AreEqual(0x0101, Cpu.Registers.PC);
        Assert.AreEqual(4L, gb.EmulatedCycles);
    }

    [TestMethod]
    public void ExecuteLoadedRomFinishesStartupBeforeRunningCartridge()
    {
        var rom = MakeRom(validLogo: true);
        rom[0x100] = 0x76; // HALT
        var gb = new Gameboy(enableDmgStartup: true);
        gb.Load(new Cartridge { Data = rom });
        int frames = 0;
        gb.Video.FrameReady += (_, _) => frames++;

        gb.ExecuteLoadedRom();

        Assert.AreEqual(264, frames);
        Assert.IsFalse(gb.IsStartupSequenceActive);
        Assert.AreEqual(0x0101, Cpu.Registers.PC);
        Assert.AreEqual(264 * 70224L + 4, gb.EmulatedCycles);
    }

    private static Gameboy CreateGameboy()
    {
        var gb = new Gameboy(enableDmgStartup: true);
        gb.Load(new Cartridge { Data = MakeRom(validLogo: true) });
        return gb;
    }

    private static byte[] MakeRom(bool validLogo = false)
    {
        var rom = new byte[0x200];
        if (validLogo) ValidLogo.CopyTo(rom, 0x104);
        return rom;
    }

    private static byte[] Render(Gameboy gb)
    {
        var pixels = new byte[160 * 144];
        DmgScreenRenderer.RenderScreen(gb.CaptureVideoState(), pixels);
        return pixels;
    }

    private static byte ExpandNibble(int nibble)
    {
        byte pixels = 0;
        for (int bit = 3; bit >= 0; bit--)
            pixels = (byte)((pixels << 2) | ((nibble >> bit & 1) * 3));
        return pixels;
    }

    private sealed class RecordingListener : TraceListener
    {
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { if (message != null) this.Messages.Add(message); }
        public override void WriteLine(string? message) { if (message != null) this.Messages.Add(message); }
    }
}
