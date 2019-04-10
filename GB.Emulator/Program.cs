using GB.Emulator.Core;
using System;
using System.Threading.Tasks;

namespace GB.Emulator
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var gameboy = new Gameboy();

            gameboy.Execute(await CartridgeLoader.Load(@"sprite.gb"));
        }
    }
}
