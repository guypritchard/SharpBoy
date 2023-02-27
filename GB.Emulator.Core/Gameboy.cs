using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class Gameboy
    {
        private readonly Cpu cpu;
        private readonly MemoryMap memory;
        private readonly Video video;
        private readonly SpriteTileManager spriteTileManager;
        private readonly BackgroundTileManager backgroundTileManager;
        private readonly Ram ramBank1;
        private readonly Ram ramBank2;
        private readonly Ram ramBank3;

        public Gameboy()
        {
            var lcd = new Lcd();
            this.spriteTileManager = new SpriteTileManager();
            this.backgroundTileManager = new BackgroundTileManager();
            this.ramBank1 = new Ram("RAM0", 0xA000, 0xBFFF);
            this.ramBank2 = new Ram("RAM1", 0xC000, 0xCFFF);
            this.ramBank3 = new Ram("RAM2", 0xD000, 0xDFFF);
            
            this.memory = new MemoryMap(
                lcd, 
                this.spriteTileManager, 
                this.backgroundTileManager,
                this.ramBank1,
                this.ramBank2,
                this.ramBank3);
            this.video = new Video(lcd);
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
