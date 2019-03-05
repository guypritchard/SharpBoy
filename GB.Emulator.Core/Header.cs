using System;
using System.Linq;
using System.Text;

namespace GB.Emulator.Core
{
    public enum CartridgeType
    {
        RomOnly = 0,
        MBC1 = 1,
        MBC1PlusRAM = 2,
        MBC1PlusRAMPlusBATTERY = 3,
    }

    public class Header
    {
        public const int Length = 0x50;
        public const int Start = 0x100;

        public static Header FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (bytes.Length != Length)
            {
                throw new ArgumentException($"Header length incorrect expected {Header.Length: X}, got {bytes.Length: X}");
            }

            return new Header
            {
                Logo = bytes.Skip(0x4).Take(0x30).ToArray(),
                TitleBytes = bytes.Skip(0x34).Take(0x10).ToArray(),
                CartridgeType = (CartridgeType)bytes[0x47],
            };
        }

        public byte[] TitleBytes { get; private set; }
        public string Title {
            get
            {
                if (this.TitleBytes != null && this.TitleBytes.Length > 0)
                {
                    return Encoding.ASCII.GetString(this.TitleBytes).Trim('\0');
                }

                return string.Empty;
            }
        }

        public byte[] Logo { get; private set; }

        public CartridgeType CartridgeType { get; private set; }
    }
}
