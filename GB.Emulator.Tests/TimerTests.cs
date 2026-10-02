using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class TimerTests
{
    [TestMethod]
    public void DividerAdvancesFromCpuCyclesAndResetsOnAnyWrite()
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = new byte[0x200] });

        for (int i = 0; i < 64; i++) gameboy.RunStep(); // 64 NOPs × 4 cycles.
        Assert.AreEqual(1, gameboy.Memory.Read8(0xFF04));
        Assert.AreEqual(1, gameboy.Memory.Peek(0xFF04));
        Assert.AreEqual(1, gameboy.Memory.Snapshot()[0xFF04]);

        gameboy.Memory.Write8(0xA5, 0xFF04);
        Assert.AreEqual(0, gameboy.Memory.Read8(0xFF04));
        for (int i = 0; i < 63; i++) gameboy.RunStep();
        Assert.AreEqual(0, gameboy.Memory.Read8(0xFF04));
        gameboy.RunStep();
        Assert.AreEqual(1, gameboy.Memory.Read8(0xFF04));
    }

    [TestMethod]
    public void CounterUsesAllFourClockSelections()
    {
        int[] periods = { 1024, 16, 64, 256 };
        for (int select = 0; select < periods.Length; select++)
        {
            var timer = new Timer();
            timer.Write8(0xFF07, (byte)(0x04 | select));
            timer.Step(periods[select] - 1);
            Assert.AreEqual(0, timer.Read8(0xFF05), $"Clock selection {select}");
            timer.Step(1);
            Assert.AreEqual(1, timer.Read8(0xFF05), $"Clock selection {select}");
        }
    }

    [TestMethod]
    public void OverflowReloadsModuloAfterFourCyclesAndRequestsInterrupt()
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = new byte[0x200] });
        gameboy.Memory.Write8(0xFF, 0xFF05);
        gameboy.Memory.Write8(0xAB, 0xFF06);
        gameboy.Memory.Write8(0x05, 0xFF07);

        for (int i = 0; i < 4; i++) gameboy.RunStep();
        Assert.AreEqual(0, gameboy.Memory.Read8(0xFF05));
        Assert.AreEqual(0, gameboy.Memory.Peek(0xFF0F) & 0x04);

        gameboy.RunStep();
        Assert.AreEqual(0xAB, gameboy.Memory.Read8(0xFF05));
        Assert.AreEqual(0x04, gameboy.Memory.Peek(0xFF0F) & 0x04);
    }

    [TestMethod]
    public void ResettingDividerOrChangingClockCanClockCounter()
    {
        var timer = new Timer();
        timer.Write8(0xFF07, 0x05);
        timer.Step(8); // Selected divider bit is high.
        timer.Write8(0xFF04, 0x99);
        Assert.AreEqual(1, timer.Read8(0xFF05));

        timer.Step(8);
        timer.Write8(0xFF07, 0x04); // Switch to low divider bit 9.
        Assert.AreEqual(2, timer.Read8(0xFF05));
    }

    [TestMethod]
    public void SnapshotRestoresDividerPhaseAndTimerRegisters()
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = new byte[0x200] });
        gameboy.Memory.Write8(0x05, 0xFF07);
        gameboy.Timer.Step(10);
        GameboyState saved = gameboy.CaptureState();

        gameboy.Timer.Step(10);
        Assert.AreEqual(1, gameboy.Memory.Read8(0xFF05));
        gameboy.RestoreState(saved);
        Assert.AreEqual(0, gameboy.Memory.Read8(0xFF05));
        Assert.AreEqual(0xFD, gameboy.Memory.Read8(0xFF07));
        gameboy.Timer.Step(6);
        Assert.AreEqual(1, gameboy.Memory.Read8(0xFF05));
    }
}
