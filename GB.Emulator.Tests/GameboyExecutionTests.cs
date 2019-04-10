using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class GameboyExecutionTests
    {
        [TestMethod]
        public async Task Gameboy_Load_Execute_Succeeds()
        {
            var gameboy = new Gameboy();

            gameboy.Execute(await CartridgeLoader.Load(Roms.TestRom));
        }
    }
}
