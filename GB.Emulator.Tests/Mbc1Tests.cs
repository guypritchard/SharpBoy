using System;
using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class Mbc1Tests
{
    [TestMethod]
    [DataRow(0x01)]
    [DataRow(0x02)]
    [DataRow(0x03)]
    public void CartridgeTypesUseMbc1AndPowerUpInBankOne(int type)
    {
        var gameboy = CreateGameboy(4, (byte)type);
        Assert.AreEqual(0, gameboy.Memory.Read8(0));
        Assert.AreEqual(1, gameboy.Memory.Read8(0x4000));
        Assert.AreEqual("Simple banking", gameboy.Memory.CartridgeBankingMode);
        Assert.IsFalse(gameboy.Memory.CartridgeRamEnabled);
        gameboy.Memory.Write8(2, 0x2000);
        Assert.AreEqual(2, gameboy.Memory.SelectedRomBank);
        Assert.AreEqual(2, gameboy.Memory.Read8(0x4000));
        Assert.AreEqual(2, gameboy.Memory.Read8(0x7FFF));
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xA000));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void BothModesRetainUpperRomBitsAndRemapForbiddenBanks(int upper)
    {
        var gameboy = CreateGameboy(128);
        gameboy.Memory.Write8((byte)(upper | 0xFC), 0x5FFF);
        gameboy.Memory.Write8(0xE0, 0x3FFF);
        Assert.AreEqual(upper * 32 + 1, gameboy.Memory.Read8(0x4000));
        Assert.AreEqual(0, gameboy.Memory.Read8(0));
        gameboy.Memory.Write8(0xFF, 0x7FFF);
        Assert.AreEqual(upper * 32, gameboy.Memory.Read8(0));
        Assert.AreEqual(upper * 32, gameboy.Memory.SelectedLowerRomBank);
        Assert.AreEqual(upper * 32, gameboy.Memory.Peek(0x3FFF));
        Assert.AreEqual(upper * 32 + 1, gameboy.Memory.Read8(0x4000));
        gameboy.Memory.Write8(0xE2, 0x2000);
        Assert.AreEqual(upper * 32 + 2, gameboy.Memory.SelectedRomBank);
        gameboy.Memory.Write8(0xFE, 0x6000);
        Assert.AreEqual(0, gameboy.Memory.Read8(0));
        Assert.AreEqual(upper * 32 + 2, gameboy.Memory.Read8(0x4000));
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    [DataRow(16)]
    public void SmallRomsIgnoreDisconnectedBitsAfterZeroTranslation(int banks)
    {
        var gameboy = CreateGameboy(banks);
        gameboy.Memory.Write8(3, 0x4000);
        gameboy.Memory.Write8(1, 0x6000);
        gameboy.Memory.Write8((byte)banks, 0x2000);
        Assert.AreEqual(0, gameboy.Memory.SelectedRomBank);
        Assert.AreEqual(0, gameboy.Memory.Read8(0x4000));
        Assert.AreEqual(0, gameboy.Memory.Read8(0));
        gameboy.Memory.Write8(0, 0x2000);
        Assert.AreEqual(1, gameboy.Memory.Read8(0x4000));
    }

    [TestMethod]
    public void RamEnableUsesLowNibbleAndDisabledWritesAreIgnored()
    {
        var gameboy = CreateGameboy(4, 2, 2);
        gameboy.Memory.Write8(0x99, 0xA000);
        Assert.AreEqual(0xFF, gameboy.Memory.Peek(0xA000));
        gameboy.Memory.Write8(0xFA, 0x1FFF);
        Assert.IsTrue(gameboy.Memory.CartridgeRamEnabled);
        Assert.AreEqual(0, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(0x42, 0xA000);
        gameboy.Memory.Write8(0x57, 0xBFFF);
        gameboy.Memory.Write8(0x0B, 0);
        Assert.IsFalse(gameboy.Memory.CartridgeRamEnabled);
        gameboy.Memory.Write8(0x99, 0xA000);
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xBFFF));
        gameboy.Memory.Write8(0x0A, 0);
        Assert.AreEqual(0x42, gameboy.Memory.Read8(0xA000));
        Assert.AreEqual(0x57, gameboy.Memory.Read8(0xBFFF));
    }

    [TestMethod]
    public void AdvancedModeBanksRamAndSimpleModeAlwaysUsesBankZero()
    {
        var gameboy = CreateGameboy(32, 2, 3);
        gameboy.Memory.Write8(0x0A, 0);
        gameboy.Memory.Write8(1, 0x6000);
        for (byte bank = 0; bank < 4; bank++)
        {
            gameboy.Memory.Write8(bank, 0x4000);
            Assert.AreEqual((int)bank, gameboy.Memory.SelectedCartridgeRamBank);
            gameboy.Memory.Write8((byte)(0x40 + bank), 0xA000);
            gameboy.Memory.Write8((byte)(0x50 + bank), 0xBFFF);
        }
        gameboy.Memory.Write8(0, 0x6000);
        Assert.AreEqual(0, gameboy.Memory.SelectedCartridgeRamBank);
        Assert.AreEqual(0x40, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(1, 0x6000);
        for (byte bank = 0; bank < 4; bank++)
        {
            gameboy.Memory.Write8(bank, 0x4000);
            Assert.AreEqual(0x40 + bank, gameboy.Memory.Peek(0xA000));
            Assert.AreEqual(0x50 + bank, gameboy.Memory.Read8(0xBFFF));
        }
    }

    [TestMethod]
    public void EightKiBRamIgnoresSecondaryBankRegister()
    {
        var gameboy = CreateGameboy(128, 2, 2);
        gameboy.Memory.Write8(0x0A, 0);
        gameboy.Memory.Write8(0x42, 0xA000);
        gameboy.Memory.Write8(1, 0x6000);
        gameboy.Memory.Write8(3, 0x4000);
        Assert.AreEqual(0, gameboy.Memory.SelectedCartridgeRamBank);
        Assert.AreEqual(0x42, gameboy.Memory.Read8(0xA000));
        Assert.AreEqual(97, gameboy.Memory.Read8(0x4000));
    }

    [TestMethod]
    [DataRow(1, 3)]
    [DataRow(2, 0)]
    public void CartridgesWithoutRamNeverExposeBackingMemory(int type, int ramCode)
    {
        var gameboy = CreateGameboy(4, (byte)type, (byte)ramCode);
        gameboy.Memory.Write8(0x0A, 0);
        gameboy.Memory.Write8(0x55, 0xA000);
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xA000));
        Assert.AreEqual(0xFF, gameboy.Memory.Peek(0xBFFF));
    }

    [TestMethod]
    public void SnapshotRestoresAllRamBanksRegistersModeAndEnableLatch()
    {
        var gameboy = CreateGameboy(32, 3, 3);
        gameboy.Memory.Write8(0x0A, 0);
        gameboy.Memory.Write8(1, 0x6000);
        for (byte bank = 0; bank < 4; bank++)
        {
            gameboy.Memory.Write8(bank, 0x4000);
            gameboy.Memory.Write8((byte)(0x40 + bank), 0xA000);
        }
        gameboy.Memory.Write8(2, 0x2000);
        gameboy.Memory.Write8(0, 0);
        GameboyState saved = gameboy.CaptureState();
        gameboy.Memory.Write8(0x0A, 0);
        for (byte bank = 0; bank < 4; bank++)
        {
            gameboy.Memory.Write8(bank, 0x4000);
            gameboy.Memory.Write8(0x99, 0xA000);
        }
        gameboy.Memory.Write8(0, 0x6000);
        gameboy.Memory.Write8(1, 0x2000);
        gameboy.RestoreState(saved);
        Assert.AreEqual(2, gameboy.Memory.SelectedRomBank);
        Assert.AreEqual(3, gameboy.Memory.SelectedCartridgeRamBank);
        Assert.AreEqual("Advanced banking", gameboy.Memory.CartridgeBankingMode);
        Assert.IsFalse(gameboy.Memory.CartridgeRamEnabled);
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(0x0A, 0);
        for (byte bank = 0; bank < 4; bank++)
        {
            gameboy.Memory.Write8(bank, 0x4000);
            Assert.AreEqual(0x40 + bank, gameboy.Memory.Read8(0xA000));
        }
        gameboy.Memory.Write8(0x99, 0xA000);
        gameboy.RestoreState(saved);
        gameboy.Memory.Write8(0x0A, 0);
        Assert.AreEqual(0x43, gameboy.Memory.Read8(0xA000));
    }

    [TestMethod]
    public void SnapshotAndDmaReadActiveRomWindows()
    {
        var gameboy = CreateGameboy(128);
        gameboy.Memory.Write8(2, 0x4000);
        gameboy.Memory.Write8(1, 0x6000);
        GameboyState saved = gameboy.CaptureState();
        byte[] snapshot = gameboy.Memory.Snapshot();
        Assert.AreEqual(64, snapshot[0]);
        Assert.AreEqual(65, snapshot[0x4000]);
        gameboy.Memory.Write8(0x40, 0xFF46);
        Assert.AreEqual(65, gameboy.Memory.Read8(0xFE00));
        Assert.AreEqual(65, gameboy.Memory.Read8(0xFE9F));
        gameboy.Memory.Write8(0, 0x6000);
        gameboy.Memory.Write8(0, 0x4000);
        gameboy.RestoreState(saved);
        Assert.AreEqual(64, gameboy.Memory.Peek(0));
        Assert.AreEqual(65, gameboy.Memory.Read8(0x4000));
    }

    [TestMethod]
    public void CpuFetchesInstructionsFromSelectedBank()
    {
        byte[] rom = CreateRom(4, 1, 0);
        rom[0x100] = 0x3E; rom[0x101] = 3; // LD A,03
        rom[0x102] = 0xEA; rom[0x103] = 0; rom[0x104] = 0x20; // LD (2000),A
        rom[0x105] = 0xC3; rom[0x106] = 0; rom[0x107] = 0x40; // JP 4000
        rom[0xC000] = 0x3E; rom[0xC001] = 0x77; // Bank 3: LD A,77
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = rom });
        for (int i = 0; i < 4; i++) gameboy.RunStep();
        Assert.AreEqual(0x77, Cpu.Registers.A);
        Assert.AreEqual(0x4002, Cpu.Registers.PC);
    }

    [TestMethod]
    public void ReloadingReplacesMapperWindowsAndResetsBankState()
    {
        var gameboy = CreateGameboy(4, 2, 3);
        gameboy.Memory.Write8(3, 0x2000);
        gameboy.Memory.Write8(0x0A, 0);
        gameboy.Memory.Write8(0x55, 0xA000);
        gameboy.Load(new Cartridge { Data = CreateRom(4, 0x13, 3) });
        Assert.AreEqual(1, gameboy.Memory.Read8(0x4000));
        gameboy.Memory.Write8(0x0A, 0);
        Assert.AreEqual(0, gameboy.Memory.Read8(0xA000));
        gameboy.Memory.Write8(2, 0x2000);
        Assert.AreEqual(2, gameboy.Memory.Read8(0x4000));
        gameboy.Load(new Cartridge { Data = CreateRom(4, 1, 0) });
        Assert.AreEqual(1, gameboy.Memory.SelectedRomBank);
        Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xA000));
        gameboy.Load(new Cartridge { Data = new byte[0x8000] });
        Assert.IsNull(gameboy.Memory.SelectedCartridgeRamBank);
        Assert.AreEqual(0, gameboy.Memory.Read8(0x4000));
    }

    private static Gameboy CreateGameboy(int banks, byte type = 1, byte ramCode = 0)
    {
        var gameboy = new Gameboy();
        gameboy.Load(new Cartridge { Data = CreateRom(banks, type, ramCode) });
        return gameboy;
    }

    private static byte[] CreateRom(int banks, byte type, byte ramCode)
    {
        var rom = new byte[banks * 0x4000];
        for (int bank = 0; bank < banks; bank++)
            Array.Fill(rom, (byte)bank, bank * 0x4000, 0x4000);
        rom[0x147] = type;
        rom[0x149] = ramCode;
        return rom;
    }
}
