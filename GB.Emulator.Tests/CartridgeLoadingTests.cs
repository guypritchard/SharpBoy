using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CartridgeLoadingTests
    {

        [TestMethod]
        [DataRow(0x01)]
        [DataRow(0x11)]
        public void LoadingBankedRomDoesNotCopyExtraBanksIntoRamOrIo(int cartridgeType)
        {
            var rom = new byte[0x10000];
            rom[0x147] = (byte)cartridgeType;
            rom[0x148] = 1;
            Array.Fill(rom, (byte)58, 0x8000, 0x8000);
            var gb = new Gameboy();
            gb.Load(new Cartridge { Data = rom });

            foreach (ushort address in new ushort[] { 0x8000, 0x9800, 0xC000, 0xFE00, 0xFF43, 0xFF80 })
                Assert.AreEqual(0, gb.Memory.Peek(address), $"Extra ROM bank leaked to ${address:X4}.");

            gb.Memory.Write8(2, 0x2000);
            Assert.AreEqual(58, gb.Memory.Read8(0x4000), "Extra banks must remain accessible through the mapper.");
            Assert.AreEqual(0, gb.CaptureVideoState().ScrollX);
        }

        [TestMethod]
        public async Task LoadCartridge_Succeeded()
        {
            Cartridge cart = await CartridgeLoader.Load(Roms.TestRom);
            Assert.AreEqual("sprite", cart.Header.Title);
            Assert.AreEqual(CartridgeType.RomOnly, cart.Header.CartridgeType);
        }
    }
}
