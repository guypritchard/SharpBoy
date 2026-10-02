using System;
using System.Collections.Generic;

namespace GB.Emulator.Core
{
    public enum CartridgeSupportLevel { Supported, Partial, Unsupported }

    /// <summary>Cartridge hardware requirements compared with the emulator's current devices.</summary>
    public sealed class CartridgeSupport
    {
        private CartridgeSupport(string typeName, string mapperName, CartridgeSupportLevel level,
            IReadOnlyList<string> requirements, IReadOnlyList<string> available,
            IReadOnlyList<string> missing, IReadOnlyList<string> notes)
        {
            this.TypeName = typeName;
            this.MapperName = mapperName;
            this.Level = level;
            this.Requirements = requirements;
            this.Available = available;
            this.Missing = missing;
            this.Notes = notes;
        }

        public string TypeName { get; }
        public string MapperName { get; }
        public CartridgeSupportLevel Level { get; }
        public IReadOnlyList<string> Requirements { get; }
        public IReadOnlyList<string> Available { get; }
        public IReadOnlyList<string> Missing { get; }
        public IReadOnlyList<string> Notes { get; }

        public static CartridgeSupport Assess(Cartridge cartridge)
        {
            ArgumentNullException.ThrowIfNull(cartridge);
            ArgumentNullException.ThrowIfNull(cartridge.Header);
            ArgumentNullException.ThrowIfNull(cartridge.Data);

            Header header = cartridge.Header;
            byte type = (byte)header.CartridgeType;
            string mapper = DescribeMapper(type);
            var requirements = new List<string> { "Game Boy cartridge ROM" };
            var available = new List<string>();
            var missing = new List<string>();
            var notes = new List<string>();
            bool mapperSupported = type is 0x00 or 0x11 or 0x12 or 0x13;
            bool mbc3TooLarge = type is >= 0x11 and <= 0x13 &&
                (cartridge.Data.Length > 0x200000 || header.RamSizeCode is not (0 or 2 or 3));

            if (mapper != "None") requirements.Add($"{mapper} bank controller");
            if (header.DeclaredRamSizeBytes > 0 || type is 0x05 or 0x06)
                requirements.Add("Cartridge RAM");

            if (type == 0x00)
                available.Add("Fixed ROM mapped at 0000–7FFF");
            else if (type is >= 0x11 and <= 0x13)
            {
                available.Add("MBC3 ROM bank switching");
                if (header.DeclaredRamSizeBytes > 0) available.Add("MBC3 RAM bank switching");
            }
            else if (type is 0x08 or 0x09) missing.Add("Unbanked external cartridge RAM");
            else missing.Add(mapper.StartsWith("Unknown", StringComparison.Ordinal)
                ? "Unknown cartridge hardware" : $"{mapper} cartridge controller");

            if (type == 0x00 && cartridge.Data.Length > 0x8000)
                missing.Add("ROM banking for a file larger than 32 KiB");
            if (mbc3TooLarge)
                missing.Add("MBC3 cartridge size beyond the implemented ROM/RAM range");

            if (HasBattery(type))
            {
                requirements.Add("Battery-backed saves");
                missing.Add("Persistent battery save files (.sav)");
            }
            if (HasRtc(type))
            {
                requirements.Add("Real-time clock");
                missing.Add("Cartridge real-time clock");
            }
            if (HasRumble(type))
            {
                requirements.Add("Rumble motor");
                missing.Add("Cartridge rumble output");
            }
            if (type == 0x22)
            {
                requirements.Add("Motion sensor");
                missing.Add("Cartridge motion sensor");
            }
            if (header.RequiresColorGameBoy)
            {
                requirements.Add("Game Boy Color hardware");
                missing.Add("Game Boy Color mode");
            }
            else if (header.SupportsColorGameBoy)
            {
                notes.Add("CGB enhancements are unavailable; the DMG-compatible mode can run.");
            }
            if (header.SupportsSuperGameBoy)
            {
                requirements.Add("Super Game Boy enhancements (optional)");
                missing.Add("Super Game Boy borders, palettes, and commands");
            }

            if (header.DeclaredRomSizeBytes is int declaredRom && declaredRom != cartridge.Data.Length)
                notes.Add($"Header declares {declaredRom / 1024} KiB ROM; file contains {cartridge.Data.Length / 1024} KiB.");
            if (header.DeclaredRomSizeBytes == null)
                notes.Add($"Unknown ROM size code 0x{header.RomSizeCode:X2}.");
            if (header.DeclaredRamSizeBytes == null)
                notes.Add($"Unknown RAM size code 0x{header.RamSizeCode:X2}.");

            CartridgeSupportLevel level = !mapperSupported || mbc3TooLarge ||
                header.RequiresColorGameBoy ||
                (type == 0x00 && cartridge.Data.Length > 0x8000)
                ? CartridgeSupportLevel.Unsupported
                : missing.Count > 0 ? CartridgeSupportLevel.Partial : CartridgeSupportLevel.Supported;
            return new CartridgeSupport(DescribeType(type), mapper, level,
                requirements, available, missing, notes);
        }

        private static string DescribeMapper(byte type) => type switch
        {
            0x00 or 0x08 or 0x09 => "None",
            >= 0x01 and <= 0x03 => "MBC1",
            0x05 or 0x06 => "MBC2",
            >= 0x0B and <= 0x0D => "MMM01",
            >= 0x0F and <= 0x13 => "MBC3",
            >= 0x19 and <= 0x1E => "MBC5",
            0x20 => "MBC6",
            0x22 => "MBC7",
            0xFC => "Pocket Camera",
            0xFD => "TAMA5",
            0xFE => "HuC3",
            0xFF => "HuC1",
            _ => $"Unknown (0x{type:X2})"
        };

        private static string DescribeType(byte type) => type switch
        {
            0x00 => "ROM only", 0x01 => "MBC1", 0x02 => "MBC1 + RAM",
            0x03 => "MBC1 + RAM + battery", 0x05 => "MBC2", 0x06 => "MBC2 + battery",
            0x08 => "ROM + RAM", 0x09 => "ROM + RAM + battery",
            0x0B => "MMM01", 0x0C => "MMM01 + RAM", 0x0D => "MMM01 + RAM + battery",
            0x0F => "MBC3 + timer + battery", 0x10 => "MBC3 + timer + RAM + battery",
            0x11 => "MBC3", 0x12 => "MBC3 + RAM", 0x13 => "MBC3 + RAM + battery",
            0x19 => "MBC5", 0x1A => "MBC5 + RAM", 0x1B => "MBC5 + RAM + battery",
            0x1C => "MBC5 + rumble", 0x1D => "MBC5 + rumble + RAM",
            0x1E => "MBC5 + rumble + RAM + battery",
            0x20 => "MBC6",
            0x22 => "MBC7 + sensor + rumble + RAM + battery",
            0xFC => "Pocket Camera", 0xFD => "TAMA5", 0xFE => "HuC3",
            0xFF => "HuC1 + RAM + battery",
            _ => $"Unknown cartridge type (0x{type:X2})"
        };

        private static bool HasBattery(byte type) => type is
            0x03 or 0x06 or 0x09 or 0x0D or 0x0F or 0x10 or 0x13 or 0x1B or 0x1E or 0x22 or 0xFF;

        private static bool HasRtc(byte type) => type is 0x0F or 0x10;

        private static bool HasRumble(byte type) => type is 0x1C or 0x1D or 0x1E or 0x22;
    }
}
