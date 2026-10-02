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
        MBC3 = 0x11,
        MBC3PlusRAM = 0x12,
        MBC3PlusRAMPlusBATTERY = 0x13,
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
                TitleBytes = bytes.Skip(0x34).Take(bytes[0x43] is 0x80 or 0xC0 ? 0x0F : 0x10).ToArray(),
                CgbFlag = bytes[0x43],
                SgbFlag = bytes[0x46],
                CartridgeType = (CartridgeType)bytes[0x47],
                RomSizeCode = bytes[0x48],
                RamSizeCode = bytes[0x49],
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

        public byte CgbFlag { get; private set; }

        public byte SgbFlag { get; private set; }

        public byte RomSizeCode { get; private set; }

        public byte RamSizeCode { get; private set; }

        public int? DeclaredRomSizeBytes => this.RomSizeCode switch
        {
            <= 8 => 32768 << this.RomSizeCode,
            0x52 => 72 * 16384,
            0x53 => 80 * 16384,
            0x54 => 96 * 16384,
            _ => null
        };

        public int? DeclaredRamSizeBytes => this.RamSizeCode switch
        {
            0 => 0,
            2 => 8192,
            3 => 32768,
            4 => 131072,
            5 => 65536,
            _ => null
        };

        public bool RequiresColorGameBoy => this.CgbFlag == 0xC0;

        public bool SupportsColorGameBoy => (this.CgbFlag & 0x80) != 0;

        public bool SupportsSuperGameBoy => this.SgbFlag == 0x03;
    }
}
