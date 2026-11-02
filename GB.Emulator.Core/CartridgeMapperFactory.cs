namespace GB.Emulator.Core
{
    /// <summary>Selects hardware from the cartridge header, independently of bus routing.</summary>
    internal static class CartridgeMapperFactory
    {
        public static CartridgeMapper Create(byte[] rom)
        {
            if (rom.Length <= 0x149) return null;
            int ramSize = rom[0x149] switch
            {
                2 => 0x2000,
                3 => 0x8000,
                _ => 0
            };
            return rom[0x147] switch
            {
                0x01 => new Mbc1(rom, 0),
                0x02 or 0x03 => new Mbc1(rom, rom.Length > 0x80000 ? System.Math.Min(ramSize, 0x2000) : ramSize),
                0x11 => new Mbc3(rom, 0),
                0x12 or 0x13 => new Mbc3(rom, ramSize),
                _ => null
            };
        }
    }
}
