using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GB.Compiler;

/// <summary>One immutable ROMX asset, placed at an explicit address in a selected bank.</summary>
public sealed record BankedAsset(string Name, string SourcePath, byte Bank, ushort Address);

public sealed record PlacedBankedAsset(string Name, int Bank, int Address, ImmutableArray<byte> Data, string Sha256);
public enum CartridgeMapper { RomOnly, Mbc1NoRam }

public sealed class CartridgeProfile
{
    public int Banks { get; }
    public CartridgeMapper Mapper { get; }
    public bool IsMbc1 => Mapper == CartridgeMapper.Mbc1NoRam;
    public int RomSizeCode => IsMbc1 ? (int)Math.Log2(Banks) - 1 : 0;
    public IReadOnlyList<PlacedBankedAsset> Assets { get; }

    private CartridgeProfile(CartridgeMapper mapper, int banks, IReadOnlyList<PlacedBankedAsset> assets)
    { Mapper = mapper; Banks = banks; Assets = assets; }

    public static CartridgeProfile RomOnly { get; } = new(CartridgeMapper.RomOnly, 2, []);

    public static CartridgeProfile CreateMbc1(int banks, IEnumerable<BankedAsset>? inputs)
    {
        if (banks is not (4 or 8 or 16 or 32))
            throw new ArgumentException("GBC008: MBC1 without RAM requires 4, 8, 16 or 32 banks.");
        var placed = new List<PlacedBankedAsset>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<BankedAsset?> nullableInputs = inputs ?? [];
        var ordered = nullableInputs.Select(asset => asset ??
            throw new ArgumentException("GBC008: asset names must be unique ASCII identifiers."))
            .OrderBy(a => a.Bank).ThenBy(a => a.Address).ThenBy(a => a.Name, StringComparer.Ordinal);
        foreach (var asset in ordered)
        {
            var name = asset.Name;
            if (name is null || !Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9_]*\z") ||
                !names.Add(name)) throw new ArgumentException("GBC008: asset names must be unique ASCII identifiers.");
            if (asset.Bank < 2 || asset.Bank >= banks || asset.Address < 0x4000 || asset.Address >= 0x8000)
                throw new ArgumentException($"GBC008: asset {asset.Name} bank/address outside configured ROMX capacity.");
            byte[] bytes;
            try { bytes = File.ReadAllBytes(asset.SourcePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { throw new ArgumentException($"GBC008: cannot read asset {asset.Name}: {ex.Message}"); }
            if (bytes.Length == 0 || (long)asset.Address + bytes.Length > 0x8000)
                throw new ArgumentException($"GBC008: asset {asset.Name} must be nonempty and fit within one ROMX bank.");
            if (placed.Any(p => p.Bank == asset.Bank && asset.Address < p.Address + p.Data.Length &&
                p.Address < asset.Address + bytes.Length))
                throw new ArgumentException($"GBC008: asset {asset.Name} overlaps another asset.");
            placed.Add(new(asset.Name, asset.Bank, asset.Address, ImmutableArray.CreateRange(bytes),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }
        return new CartridgeProfile(CartridgeMapper.Mbc1NoRam, banks, placed.AsReadOnly());
    }

    public bool ContainsCopy(IrBankCopy copy) => IsMbc1 && copy.Bank >= 2 && copy.Bank < Banks &&
        copy.Length > 0 && copy.SourceAddress >= 0x4000 && (long)copy.SourceAddress + copy.Length <= 0x8000 &&
        (long)copy.BufferOffset + copy.Length <= 0x800 &&
        Assets.Any(a => a.Bank == copy.Bank && copy.SourceAddress >= a.Address &&
            (long)copy.SourceAddress + copy.Length <= (long)a.Address + a.Data.Length);

    public bool NeedsCapacityGuard => IsMbc1 && !Assets.Any(a => a.Bank == Banks - 1 &&
        a.Address + a.Data.Length == 0x8000);

    public string Assembly()
    {
        if (!IsMbc1) return "";
        var result = new StringBuilder();
        foreach (var asset in Assets)
        {
            result.AppendLine("PUSHS");
            result.AppendLine($"SECTION \"GB asset {asset.Name}\", ROMX[${asset.Address:X4}], BANK[{asset.Bank}]");
            result.AppendLine($"gb_asset_{asset.Name}::");
            foreach (var chunk in asset.Data.Chunk(16))
                result.AppendLine("    db " + string.Join(", ", chunk.Select(b => $"${b:X2}")));
            result.AppendLine($"gb_asset_{asset.Name}_end::");
            result.AppendLine("POPS");
        }
        // An asset ending at $8000 already forces full capacity; never overlap it.
        if (NeedsCapacityGuard)
        {
            result.AppendLine("PUSHS");
            result.AppendLine($"SECTION \"GB cartridge capacity\", ROMX[$7FFF], BANK[{Banks - 1}]");
            result.AppendLine("gb_capacity_guard::");
            result.AppendLine("    db $00");
            result.AppendLine("POPS");
        }
        return result.ToString();
    }

    public string Report(int bankOneBytes)
    {
        var result = new StringBuilder($"Cartridge {(IsMbc1 ? "MBC1 without RAM" : "ROM-only")} {Banks} banks ({Banks * 16} KiB); type ${(IsMbc1 ? 1 : 0):X2}, ROM size ${RomSizeCode:X2}, RAM $00\n");
        for (var bank = 0; bank < Banks; bank++)
        {
            var used = bank == 1 ? bankOneBytes : 0;
            if (bank == Banks - 1 && NeedsCapacityGuard) used++;
            used += Assets.Where(a => a.Bank == bank).Sum(a => a.Data.Length);
            result.AppendLine($"ROM bank {bank}: assets/guard used {used}, free {0x4000 - used} (ROM0 code/metadata reported separately)");
        }
        foreach (var a in Assets)
            result.AppendLine($"asset {a.Name} bank {a.Bank} ${a.Address:X4} size {a.Data.Length} SHA-256 {a.Sha256}");
        return result.ToString();
    }
}
