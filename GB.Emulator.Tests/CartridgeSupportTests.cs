using System.Linq;
using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class CartridgeSupportTests
{
    [TestMethod]
    public void PokemonHeaderReportsHardwareAndMissingFeatures()
    {
        Cartridge cartridge = CreateCartridge(0x13, 0x05, 0x03, 1024 * 1024, sgb: 0x03);
        Header header = cartridge.Header;
        Assert.AreEqual(1024 * 1024, header.DeclaredRomSizeBytes);
        Assert.AreEqual(32768, header.DeclaredRamSizeBytes);
        Assert.IsTrue(header.SupportsSuperGameBoy);

        CartridgeSupport support = CartridgeSupport.Assess(cartridge);
        Assert.AreEqual("MBC3", support.MapperName);
        Assert.AreEqual(CartridgeSupportLevel.Partial, support.Level);
        Assert.IsTrue(support.Available.Contains("MBC3 ROM bank switching"));
        Assert.IsTrue(support.Missing.Any(item => item.Contains("save files")));
        Assert.IsTrue(support.Missing.Any(item => item.Contains("Super Game Boy")));
    }

    [TestMethod]
    public void RomOnlyCartridgeIsSupportedAndMbc1IsReportedUnsupported()
    {
        Cartridge plain = CreateCartridge(0, 0, 0, 32768);
        Assert.AreEqual(CartridgeSupportLevel.Supported, CartridgeSupport.Assess(plain).Level);

        Cartridge mbc1 = CreateCartridge(1, 1, 0, 65536);
        CartridgeSupport support = CartridgeSupport.Assess(mbc1);
        Assert.AreEqual(CartridgeSupportLevel.Unsupported, support.Level);
        Assert.IsTrue(support.Missing.Any(item => item.Contains("MBC1")));
    }

    [TestMethod]
    public void ColorOnlyCartridgeIsReportedUnsupported()
    {
        Cartridge cartridge = CreateCartridge(0, 0, 0, 32768, cgb: 0xC0);
        Assert.IsTrue(cartridge.Header.RequiresColorGameBoy);
        Assert.AreEqual(CartridgeSupportLevel.Unsupported, CartridgeSupport.Assess(cartridge).Level);
    }

    private static Cartridge CreateCartridge(byte type, byte romSize, byte ramSize,
        int length, byte cgb = 0, byte sgb = 0)
    {
        var bytes = new byte[length];
        bytes[0x143] = cgb;
        bytes[0x146] = sgb;
        bytes[0x147] = type;
        bytes[0x148] = romSize;
        bytes[0x149] = ramSize;
        return new Cartridge
        {
            Data = bytes,
            Header = Header.FromBytes(bytes.Skip(0x100).Take(Header.Length).ToArray())
        };
    }
}
