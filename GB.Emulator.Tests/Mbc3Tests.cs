using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class Mbc3Tests
{
    [TestMethod]
    public void RomBanksSwitchAndZeroSelectsBankOne()
    {
        var gameboy = CreateGameboy();
        Assert.AreEqual(0x11, gameboy.Memory.Read8(0x4000));
        gameboy.Memory.Write8(2, 0x2000);
        Assert.AreEqual(2, gameboy.Memory.SelectedRomBank);
        Assert.AreEqual(0x22, gameboy.Memory.Read8(0x4000));
        Assert.AreEqual(0x22, gameboy.Memory.Peek(0x4000));
        gameboy.Memory.Write8(0, 0x2000);
        Assert.AreEqual(0x11, gameboy.Memory.Read8(0x4000));
    }

    [TestMethod]
    public void ExternalRamNeedsEnableAndHasIndependentBanks()
    {
        var gameboy = CreateGameboy();
        gameboy.Memory.Write8(0x99, 0xA000);
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(0x0A, 0x0000);
        gameboy.Memory.Write8(0x42, 0xA000);
        gameboy.Memory.Write8(1, 0x4000);
        Assert.AreEqual(1, gameboy.Memory.SelectedCartridgeRamBank);
        Assert.AreEqual(0, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(0x57, 0xA000);
        gameboy.Memory.Write8(0, 0x4000);
        Assert.AreEqual(0x42, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(1, 0x4000);
        Assert.AreEqual(0x57, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(0, 0x0000);
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xA000));
    }

    [TestMethod]
    public void CpuFetchesInstructionsFromSelectedBank()
    {
        byte[] rom = CreateRom();
        rom[0x0100] = 0x3E; rom[0x0101] = 2;             // LD A,02
        rom[0x0102] = 0xEA; rom[0x0103] = 0; rom[0x0104] = 0x20; // LD (2000),A
        rom[0x0105] = 0xC3; rom[0x0106] = 0; rom[0x0107] = 0x40; // JP 4000
        rom[0x8000] = 0x3E; rom[0x8001] = 0x77;        // Bank 2: LD A,77
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = rom });
        for (int i = 0; i < 4; i++) gameboy.RunStep();
        Assert.AreEqual(0x77, Cpu.Registers.A);
        Assert.AreEqual(0x4002, Cpu.Registers.PC);
    }

    [TestMethod]
    public void SnapshotRestoresBankSelectionAndCartridgeRam()
    {
        var gameboy = CreateGameboy();
        gameboy.Memory.Write8(0x0A, 0x0000);
        gameboy.Memory.Write8(2, 0x2000);
        gameboy.Memory.Write8(3, 0x4000);
        gameboy.Memory.Write8(0xAC, 0xA000);
        GameboyState state = gameboy.CaptureState();
        gameboy.Memory.Write8(1, 0x2000);
        gameboy.Memory.Write8(0x55, 0xA000);
        gameboy.RestoreState(state);
        Assert.AreEqual(0x22, gameboy.Memory.Read8(0x4000));
        Assert.AreEqual(0xAC, gameboy.Memory.Read8(0xA000));
    }

    private static Gameboy CreateGameboy()
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = CreateRom() });
        return gameboy;
    }

    private static byte[] CreateRom()
    {
        var rom = new byte[4 * 0x4000];
        rom[0x147] = 0x13;
        rom[0x149] = 0x03;
        rom[0x4000] = 0x11;
        rom[0x8000] = 0x22;
        return rom;
    }
}
