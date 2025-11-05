using GB.Emulator.Core;
using System;
using System.Threading.Tasks;

namespace GB.Emulator
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("This emulator requires Windows to render video output.");
                return;
            }

            var gameboy = new Gameboy();
            var cartridge = await CartridgeLoader.Load(@"..\..\..\..\GB.Roms\Sprite.gb");

            gameboy.Execute(cartridge);
        }
    }
}
