using System;
using System.IO;
using System.Threading.Tasks;

namespace GB.Emulator.Core
{
    public static class CartridgeLoader
    {
        public static async Task<Cartridge> Load(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            try
            {
                using (FileStream fs = File.OpenRead(fileName))
                {
                    return await CartridgeLoader.Load(fs);
                }
            }
            catch (IOException e)
            {
                throw new CartridgeLoadException($"Unable to load cartrige '{fileName}'", e);
            }
        }

        public static async Task<Cartridge> Load(Stream stream)
        {
            byte[] headerBytes = new byte[Header.Length];

            long position = stream.Seek(Header.Start, SeekOrigin.Begin);
            if (position != Header.Start)
            {
                throw new CartridgeLoadException("Unable to seek to the cartridge header.");
            }

            int read = await stream.ReadAsync(headerBytes, 0, Header.Length);
            if (read != Header.Length)
            {
                throw new CartridgeLoadException("Unable to read the entire cartridge header.");
            }

            stream.Seek(0, SeekOrigin.Begin);

            var entireCartridge = new byte[stream.Length];
            read = await stream.ReadAsync(entireCartridge, 0, (int)stream.Length);
            if (read != stream.Length)
            {
                throw new CartridgeLoadException("Unable to read the entire cartridge.");
            }

            return new Cartridge
            {
                Header = Header.FromBytes(headerBytes),
                Data = entireCartridge
            };
        }
    }
}
