using GB.Compiler;

string? output = null, title = null, rgbdsBin = null;
byte[]? backgroundTiles = null, backgroundMap = null, spriteTiles = null;
bool keep = false, probe = false;
int? mbc1Banks = null;
int wramStart = 0xC000;
bool textFont = false;
var assets = new List<BankedAsset>();
var sources = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--background-tiles":
        case "--background-map":
        case "--sprite-tiles":
            var option = args[i];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            { Console.Error.WriteLine($"GBC003: {option} requires a FILE argument."); return 2; }
            var path = args[++i];
            byte[] data;
            try { data = await File.ReadAllBytesAsync(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { Console.Error.WriteLine($"GBC001: Cannot read {option} file '{path}': {ex.Message}"); return 2; }
            if (option == "--background-tiles") backgroundTiles = data;
            else if (option == "--background-map") backgroundMap = data;
            else spriteTiles = data;
            break;
        case "--text-font": textFont = true; break;
        case "--wram-start" when i + 1 < args.Length && int.TryParse(args[i + 1], out var start):
            wramStart = start; i++; break;
        case "--out" when i + 1 < args.Length: output = args[++i]; break;
        case "--title" when i + 1 < args.Length: title = args[++i]; break;
        case "--rgbds-bin" when i + 1 < args.Length: rgbdsBin = args[++i]; break;
        case "--keep-intermediates": keep = true; break;
        case "--check-rgbds": probe = true; break;
        case "--mbc1-banks" when i + 1 < args.Length && int.TryParse(args[i + 1], out var bankCount):
            mbc1Banks = bankCount; i++; break;
        case "--banked-asset" when i + 1 < args.Length:
            // NAME:BANK:ADDRESS:FILE, decimal bank and $hex or decimal CPU address.
            var fields = args[++i].Split(':', 4);
            var addressText = fields.Length == 4 ? fields[2] : "";
            var hex = addressText.StartsWith('$');
            if (fields.Length != 4 || !byte.TryParse(fields[1], out var bank) ||
                !ushort.TryParse(hex ? addressText[1..] : addressText,
                    hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var address))
            { Console.Error.WriteLine("GBC003: --banked-asset expects NAME:BANK:$ADDRESS:FILE (or decimal ADDRESS)."); return 2; }
            assets.Add(new BankedAsset(fields[0], fields[3], bank, address));
            break;
        default:
            if (args[i].EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) sources.Add(args[i]);
            else { Console.Error.WriteLine($"GBC003: Unknown argument {args[i]}"); return 2; }
            break;
    }
}
if (probe && sources.Count == 0 && output is null)
{
    var issues = await new RgbdsProbe(new SystemToolRunner(), rgbdsBin).CheckAsync();
    foreach (var issue in issues) Console.Error.WriteLine(issue);
    if (issues.Count == 0) Console.WriteLine($"RGBDS {RgbdsProbe.RequiredVersion}: rgbasm, rgblink and rgbfix found.");
    return issues.Count == 0 ? 0 : 1;
}
if (sources.Count == 0 || output is null || probe)
{
    Console.Error.WriteLine("Usage: GB.Compiler.Cli --out game.gb [--title TITLE] [--rgbds-bin DIR] [--keep-intermediates] [--text-font] [--wram-start DECIMAL_ADDRESS] [--background-tiles FILE --background-map FILE] [--sprite-tiles FILE] [--mbc1-banks 4|8|16|32 --banked-asset 'NAME:BANK:$ADDRESS:FILE' ...] source.cs [other.cs ...] (or --check-rgbds)");
    Console.Error.WriteLine("MBC1: no RAM, assets in physical bank 2+, ROMX address $4000-$7FFF; quote $ addresses in PowerShell. Code is ROM0, bank 1 reserved for display, $D000-$D7FF is constant-only staging. No function banking, save RAM, >32 banks, interrupts, DMA/VRAM transfer, reentrancy or managed arrays.");
    return 2;
}
CartridgeProfile profile;
try
{
    if (mbc1Banks is null && assets.Count != 0)
        throw new ArgumentException("GBC008: --banked-asset requires --mbc1-banks.");
    profile = mbc1Banks is int count ? CartridgeProfile.CreateMbc1(count, assets) : CartridgeProfile.RomOnly;
}
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
if (textFont)
{
    if (backgroundTiles is not null || backgroundMap is not null)
    { Console.Error.WriteLine("GBC009: --text-font cannot be combined with explicit background assets."); return 2; }
    backgroundTiles = GB.Sdk.GameFont.CreateTiles();
    backgroundMap = Enumerable.Repeat((byte)32, 1024).ToArray();
}
var result = await new RomCompiler().CompileAsync(sources, new RomBuildOptions(output, title ?? "SHARPBOY", rgbdsBin, keep, profile,
    new GameAssetBundle(backgroundTiles, backgroundMap, spriteTiles), Limits: new Sm83Limits(WramStart: wramStart, WramBytes: 0xE000 - wramStart)));
foreach (var diagnostic in result.Diagnostics) Console.Error.WriteLine(diagnostic);
foreach (var warning in result.Warnings) Console.Error.WriteLine(warning);
if (result.WorkDirectory is not null) Console.Error.WriteLine($"Build work directory: {result.WorkDirectory}");
if (!result.Success) return 1;
foreach (var artifact in result.Artifacts) Console.WriteLine($"{artifact.Key}: {artifact.Value}");
return 0;
