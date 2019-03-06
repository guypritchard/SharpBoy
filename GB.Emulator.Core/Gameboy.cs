using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace GB.Emulator.Core
{
    public class Gameboy
    {
        private readonly Cpu cpu;
        private readonly Memory memory;
        private readonly Video video;

        public Gameboy()
        {
            this.memory = new Memory();
            this.video = new Video();
            this.cpu = new Cpu(this.memory, this.video);
        }

        public void Execute(Cartridge cartridge)
        {
            this.Execute(cartridge.Data);
        }

        private void Execute(byte[] code)
        {
            this.cpu.Execute(code);
        }
    }
}
