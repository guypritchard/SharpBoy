using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GB.Emulator.AI.Tetris;
using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using GB.Emulator.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class TetrisAiTests
{
    private static readonly Cell[][] Square = [[new(0, 0), new(1, 0), new(0, 1), new(1, 1)]];
    private static readonly Cell[][] Bar =
    [
        [new(0, 0), new(1, 0), new(2, 0), new(3, 0)],
        [new(0, 0), new(0, 1), new(0, 2), new(0, 3)]
    ];

    [TestMethod]
    public void FindsReachableFourLineClearWithoutMutatingObservation()
    {
        var board = new bool[200];
        for (int y = 16; y < 20; y++)
            for (int x = 0; x < 10; x++) board[y * 10 + x] = x != 9;
        var original = (bool[])board.Clone();
        var plan = new TetrisPlanner().Choose(new(board, Bar, 3, 0, 0));
        Assert.IsNotNull(plan);
        Assert.AreEqual(4, plan.Lines);
        Assert.IsTrue(plan.Landing.All(c => c.X == 9));
        Assert.IsTrue(plan.Moves.Contains(TetrisMove.Clockwise) || plan.Moves.Contains(TetrisMove.Counterclockwise));
        CollectionAssert.AreEqual(original, board);
    }

    [TestMethod]
    public void SearchDoesNotTeleportThroughAFullHeightWall()
    {
        var board = new bool[200];
        for (int y = 0; y < 20; y++) board[y * 10 + 5] = true;
        var plan = new TetrisPlanner().Choose(new(board, Square, 1, 0, 0));
        Assert.IsNotNull(plan);
        Assert.IsTrue(plan.Landing.All(c => c.X < 5));
    }

    [TestMethod]
    public void InvalidSpawnCannotGenerateJoypadMoves()
    {
        var board = Enumerable.Repeat(true, 200).ToArray();
        Assert.IsNull(new TetrisPlanner().Choose(new(board, Square, 3, 0, 0)));
    }

    [TestMethod]
    public void ClearLineSliderChangesTheSelectedPlacement()
    {
        var board = new bool[200];
        for (int x = 2; x < 10; x++) board[190 + x] = true;
        var observation = new TetrisObservation(board, Square, 3, 0, 0);
        var neutral = new TetrisAiSettings { ClearLines = 0, AvoidHoles = 0, LowStack = 0, FlatSurface = 0 };
        var planner = new TetrisPlanner();
        var quick = planner.Choose(observation, neutral);
        var scoring = planner.Choose(observation, neutral with { ClearLines = 4 });
        Assert.IsNotNull(quick); Assert.IsNotNull(scoring);
        Assert.AreEqual(0, quick.Lines);
        Assert.AreEqual(1, scoring.Lines);
        Assert.IsTrue(scoring.Landing.All(cell => cell.X < 2));
    }

    [TestMethod]
    public void PreviewLookaheadLeavesTheFourCellGapForTheIncomingBar()
    {
        var board = new bool[200];
        for (int x = 2; x < 6; x++) board[190 + x] = true;
        var observation = new TetrisObservation(board, Square, 8, 0, 0, Bar, NextY: 0);
        var neutral = new TetrisAiSettings { ClearLines = 4, AvoidHoles = 0, LowStack = 0, FlatSurface = 0 };
        var planner = new TetrisPlanner();
        var immediate = planner.Choose(observation, neutral);
        var lookahead = planner.Choose(observation, neutral with { Lookahead = 1 });
        Assert.IsNotNull(immediate); Assert.IsNotNull(lookahead);
        Assert.IsTrue(immediate.Landing.All(cell => cell.X >= 6));
        Assert.IsTrue(lookahead.Landing.All(cell => cell.X < 2));
        Assert.AreEqual(0, lookahead.Lines, "The payoff comes from the preview, not this piece.");
        Assert.IsGreaterThan(immediate.Score, lookahead.Score);
    }

    [TestMethod]
    public void SettingsRoundTripAndRejectInvalidTimingsWeightsAndVersions()
    {
        foreach (var preset in TetrisAiSettings.Presets.Values) preset.Validate();
        string file = Path.GetTempFileName();
        try
        {
            var settings = TetrisAiSettings.Presets["Strategist"] with { AvoidHoles = 3.25, ReleaseFrames = 8 };
            settings.Save(file);
            Assert.AreEqual(settings, TetrisAiSettings.Load(file));
            Assert.ThrowsExactly<ArgumentException>(() => (settings with { HoldFrames = 0 }).Validate());
            Assert.ThrowsExactly<ArgumentException>(() => (settings with { Lookahead = double.NaN }).Validate());
            Assert.ThrowsExactly<ArgumentException>(() => (settings with { Version = 99 }).Validate());
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void AbandonedLookaheadCanBeCancelled()
    {
        using var stop = new System.Threading.CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => new TetrisPlanner().Choose(
            new(new bool[200], Square, 3, 0, 0, Bar), TetrisAiSettings.Presets["Strategist"], stop.Token));
    }

    [TestMethod]
    public void BoardReaderRequiresMatchingRomAndReadsDetachedRam()
    {
        var rom = new byte[32768];
        var profile = new TetrisMemoryProfile
        {
            RomSha256 = Convert.ToHexString(SHA256.HashData(rom)),
            BoardAddress = 0xC000,
            EmptyCell = 0,
            XAddress = 0xFF80,
            YAddress = 0xFF81,
            PieceAddress = 0xFF82,
            RotationAddress = 0xFF83,
            StateAddress = 0xFF84,
            PlayingState = 0,
            Pieces = new() { [0] = Square }
        };
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(profile));
            var reader = TetrisMemoryProfile.Load(file, rom);
            using var host = Host(rom);
            var observation = reader.Observe(host.Inspection);
            Assert.IsNotNull(observation);
            Assert.HasCount(200, observation.Board);
            Assert.IsTrue(observation.Board.All(c => !c));
            rom[42] = 1;
            Assert.ThrowsExactly<InvalidDataException>(() => TetrisMemoryProfile.Load(file, rom));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void ControllerReleasesButtonsWhenPausedAndDoesNotAdvanceWhileGuestTimeIsStill()
    {
        using var host = Host(new byte[32768]);
        host.Resume();
        var observer = new ConstantObserver();
        var controller = new TetrisController(observer);
        controller.Tick(host);
        for (int i = 0; i < 20; i++) controller.Tick(host);
        Assert.AreEqual(0, observer.Calls, "UI polling must not drive the bot ahead of guest time.");
        host.Advance(TimeSpan.FromSeconds(1.0 / 30));
        controller.Tick(host);
        for (int i = 0; i < 100 && controller.Plan == null; i++)
        {
            System.Threading.Thread.Sleep(2);
            host.Advance(TimeSpan.FromSeconds(1.0 / 60)); controller.Tick(host);
        }
        Assert.IsNotNull(controller.Plan);
        Assert.IsTrue(Enum.GetValues<GameBoyButton>().Any(host.IsPressed));
        controller.Configure(TetrisAiSettings.Presets["Strategist"] with { ReleaseFrames = 8 });
        host.Advance(TimeSpan.FromSeconds(1.0 / 60)); controller.Tick(host);
        Assert.IsFalse(Enum.GetValues<GameBoyButton>().Any(host.IsPressed), "A live style change must release the old policy's held button.");
        Assert.IsNull(controller.Plan);
        host.Pause(); controller.Tick(host);
        Assert.IsNull(controller.Plan);
        Assert.IsFalse(Enum.GetValues<GameBoyButton>().Any(host.IsPressed));
    }

    private sealed class ConstantObserver : ITetrisObserver
    {
        public int Calls;
        public TetrisObservation Observe(EmulationInspection inspection)
        { Calls++; return new(new bool[200], Square, 3, 0, 0); }
    }

    private static EmulationHost Host(byte[] rom)
    {
        var host = new EmulationHost(new Gameboy(enableDmgStartup: false));
        host.Load(new Cartridge { Data = rom, Header = Header.FromBytes(rom.AsSpan(0x100, 0x50).ToArray()) });
        return host;
    }
}
