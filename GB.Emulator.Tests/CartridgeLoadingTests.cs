using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CartridgeLoadingTests
    {

        [TestMethod]
        public async Task LoadCartridge_Succeeded()
        {
            Cartridge cart = await CartridgeLoader.Load(Roms.TestRom);
            Assert.AreEqual("sprite", cart.Header.Title);
            Assert.AreEqual(CartridgeType.RomOnly, cart.Header.CartridgeType);
        }
    }
}
