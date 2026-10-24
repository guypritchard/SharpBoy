using System.Diagnostics;
using System.Security.Cryptography;
using GB.Compiler;
using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class BankedAssetsTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static string Source => File.ReadAllText(Fixture("BankedAssets.cs"));
    private static CartridgeProfile Profile(int banks = 4) => CartridgeProfile.CreateMbc1(banks,
        [new("Tail", Fixture("tail.bin"), 2, 0x7FFE), new("Data", Fixture("data.bin"), 2, 0x4000)]);
    private static IrModule Lower(string source) => GameIrCompiler.AnalyzeAndLower([("banked.cs", source)]);
    private static string Program(string body) =>
        "using GB.Sdk; static class Game { [GameEntryPoint] static void Start() { " + body + " } }";

    [TestMethod]
    public void ExplicitProfilesHaveExpectedMapperSizeAndCapacity()
    {
        var normal = CartridgeProfile.RomOnly;
        Assert.AreEqual(CartridgeMapper.RomOnly, normal.Mapper);
        Assert.AreEqual(2, normal.Banks);
        Assert.AreEqual(0, normal.RomSizeCode);
        Assert.IsFalse(normal.IsMbc1);
        foreach (var (banks, size) in new[] { (4, 1), (8, 2), (16, 3), (32, 4) })
        {
            var profile = CartridgeProfile.CreateMbc1(banks, null);
            Assert.IsTrue(profile.IsMbc1);
            Assert.AreEqual(CartridgeMapper.Mbc1NoRam, profile.Mapper);
            Assert.AreEqual(banks, profile.Banks);
            Assert.AreEqual(size, profile.RomSizeCode);
            StringAssert.Contains(profile.Report(0), $"{banks * 16} KiB); type $01, ROM size ${size:X2}, RAM $00");
            StringAssert.Contains(profile.Assembly(), $"ROMX[$7FFF], BANK[{banks - 1}]");
        }
        foreach (var banks in new[] { -1, 0, 1, 2, 3, 5, 33, 64 })
            StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => CartridgeProfile.CreateMbc1(banks, [])).Message, "GBC008");
    }

    [TestMethod]
    public void AssetsAreSortedHashedAndReportedDeterministically()
    {
        var profile = Profile();
        CollectionAssert.AreEqual(new[] { "Data", "Tail" }, profile.Assets.Select(a => a.Name).ToArray());
        foreach (var asset in profile.Assets)
        {
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(asset.Data.AsSpan())).ToLowerInvariant(), asset.Sha256);
            StringAssert.Contains(profile.Report(0), $"asset {asset.Name} bank {asset.Bank} ${asset.Address:X4} size {asset.Data.Length} SHA-256 {asset.Sha256}");
            StringAssert.Contains(profile.Assembly(), $"SECTION \"GB asset {asset.Name}\", ROMX[${asset.Address:X4}], BANK[{asset.Bank}]");
        }
        Assert.AreEqual(profile.Assembly(), CartridgeProfile.CreateMbc1(4,
            [new("Data", Fixture("data.bin"), 2, 0x4000), new("Tail", Fixture("tail.bin"), 2, 0x7FFE)]).Assembly());
        Assert.IsLessThan(profile.Assembly().IndexOf("gb_asset_Tail::", StringComparison.Ordinal),
            profile.Assembly().IndexOf("gb_asset_Data::", StringComparison.Ordinal));
    }

    [TestMethod]
    public void InvalidManifestNeverProducesProfile()
    {
        var data = Fixture("data.bin");
        var good = new BankedAsset("Data", data, 2, 0x4000);
        var cases = new BankedAsset[][]
        {
            [good, good], [new("", data, 2, 0x4000)], [new("_bad", data, 2, 0x4000)],
            [new("bad-name", data, 2, 0x4000)], [new("é", data, 2, 0x4000)],
            [new(null!, data, 2, 0x4000)], [null!],
            [new("Bad", data, 0, 0x4000)], [new("Bad", data, 1, 0x4000)],
            [new("Bad", data, 4, 0x4000)], [new("Bad", data, 2, 0x3FFF)],
            [new("Bad", data, 2, 0x8000)], [new("Missing", data + ".missing", 2, 0x4000)],
            [new("Overlap", data, 2, 0x4005), good], [new("Cross", data, 2, 0x7FFB)]
        };
        foreach (var assets in cases)
            StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => CartridgeProfile.CreateMbc1(4, assets)).Message, "GBC008");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var empty = Path.Combine(folder, "empty.bin");
            var oversized = Path.Combine(folder, "oversized.bin");
            File.WriteAllBytes(empty, []);
            File.WriteAllBytes(oversized, new byte[0x4001]);
            foreach (var file in new[] { empty, oversized })
                StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() =>
                    CartridgeProfile.CreateMbc1(4, [new("Bad", file, 2, 0x4000)])).Message, "GBC008");
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void CopySpanMustBelongToExactlyOneAssetAndFitStaging()
    {
        var profile = Profile();
        foreach (var (bank, address, offset, length) in new[]
        {
            (2, 0x4000, 0, 6), (2, 0x4001, 0x7FD, 3), (2, 0x7FFE, 0x7FF, 1)
        }) Assert.IsTrue(profile.ContainsCopy(Copy(bank, address, offset, length)));
        foreach (var (bank, address, offset, length) in new[]
        {
            (1, 0x4000, 0, 1), (3, 0x4000, 0, 1), (4, 0x4000, 0, 1),
            (2, 0x3FFF, 0, 1), (2, 0x4006, 0, 1), (2, 0x4005, 0, 2),
            (2, 0x7FFD, 0, 2), (2, 0x7FFF, 0, 2), (2, 0x8000, 0, 1),
            (2, 0x4000, 0, 0), (2, 0x4000, 0x7FF, 2)
        }) Assert.IsFalse(profile.ContainsCopy(Copy(bank, address, offset, length)), $"{bank}:{address:X4}+{length}, buffer {offset:X4}");
        Assert.IsFalse(CartridgeProfile.RomOnly.ContainsCopy(Copy(2, 0x4000, 0, 1)));
    }

    private static IrBankCopy Copy(int bank, int address, int offset, int length) =>
        new((byte)bank, (ushort)address, (ushort)offset, (ushort)length, new IrLocation("test.cs", 1, 1, 0, 1));

    [TestMethod]
    public void Mbc1RawWritesRequireProvenUnreservedAddress()
    {
        var profile = Profile();
        foreach (var address in new[] { 0, 0x2000, 0x4000, 0x6000, 0x7FFF, 0xC000, 0xC001, 0xD000, 0xDF80, 0xE000, 0xF000 })
        {
            var module = Lower(Program($"GameMemory.WriteByte(0x{address:X4}, 1);"));
            var rejected = Sm83Backend.Emit(module, profile: profile);
            Assert.AreEqual("GBB007", rejected.Diagnostics.Single().Id, $"${address:X4}");
            Assert.IsGreaterThan(0, rejected.Diagnostics.Single().Source!.Line);
            Assert.IsNull(rejected.Assembly);
            Assert.IsTrue(Sm83Backend.Emit(module).Success, $"ROM-only ${address:X4}");
        }
        var dynamic = Lower(Program("ushort address = 0xC100; GameMemory.WriteByte(address, 1);"));
        Assert.AreEqual("GBB007", Sm83Backend.Emit(dynamic, profile: profile).Diagnostics.Single().Id);
        Assert.IsTrue(Sm83Backend.Emit(dynamic).Success);
        Assert.IsTrue(Sm83Backend.Emit(Lower(Program("GameMemory.WriteByte(0xC100, 1);")), profile: profile).Success);
        var folded = Lower(Program("const ushort start = 0xC100; GameMemory.WriteByte(start + 1, 1);"));
        var foldedResult = Sm83Backend.Emit(folded, profile: profile);
        Assert.AreEqual("GBB007", foldedResult.Diagnostics.Single().Id); // local loads remain mutable IR
        var arithmetic = Lower(Program("GameMemory.WriteByte(0xC100 + 1, 1);"));
        Assert.IsTrue(Sm83Backend.Emit(arithmetic, profile: profile).Success);
    }

    [TestMethod]
    public void LastBankAssetOwnsCapacityByteAndBytesRemainFrozen()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-asset-freeze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "last.bin");
            var original = new byte[] { 0xA4, 0xB5 };
            File.WriteAllBytes(path, original);
            foreach (var banks in new[] { 4, 8, 16, 32 })
            {
                var profile = CartridgeProfile.CreateMbc1(banks, [new("Last", path, (byte)(banks - 1), 0x7FFE)]);
                Assert.IsFalse(profile.NeedsCapacityGuard);
                Assert.IsFalse(profile.Assembly().Contains("gb_capacity_guard", StringComparison.Ordinal));
                Assert.HasCount(2, profile.Assets.Single().Data);
                var hash = profile.Assets.Single().Sha256;
                original[0] = 0;
                File.WriteAllBytes(path, [0, 0]);
                var exposed = profile.Assets.Single().Data.ToArray();
                exposed[1] = 0;
                Assert.AreEqual((byte)0xA4, profile.Assets.Single().Data[0]);
                Assert.AreEqual((byte)0xB5, profile.Assets.Single().Data[1]);
                Assert.AreEqual(Convert.ToHexString(SHA256.HashData(new byte[] { 0xA4, 0xB5 })).ToLowerInvariant(), hash);
                StringAssert.Contains(profile.Assembly(), "db $A4, $B5");
                Assert.IsFalse(profile.Report(0).Contains("assets/guard used 3,", StringComparison.Ordinal));
                File.WriteAllBytes(path, [0xA4, 0xB5]);
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public void FrontendOnlyAcceptsConstantSdkTypedBankOperations()
    {
        var good = Program("const byte bank = 2; const ushort start = 0x4000; GameBanking.CopyToBuffer(bank, start + 1, 0, 3); GameMemory.WriteByte(0xC100, GameBanking.ReadBufferByte(1 + 2));");
        Assert.IsTrue(GameFrontend.Analyze([("banked.cs", good)]).Success);
        Assert.IsTrue(Sm83Backend.Emit(Lower(good), profile: Profile()).Success);
        foreach (var body in new[]
        {
            "byte bank = 2; GameBanking.CopyToBuffer(bank, 0x4000, 0, 1);",
            "ushort offset = 0; GameBanking.CopyToBuffer(2, 0x4000, offset, 1);",
            "ushort offset = 1; GameMemory.WriteByte(0xC100, GameBanking.ReadBufferByte(offset));"
        })
        {
            var frontend = GameFrontend.Analyze([("banked.cs", Program(body))]);
            Assert.IsTrue(frontend.Diagnostics.Any(d => d.Id == "GBL009" && d.Line > 0), string.Join("\n", frontend.Diagnostics));
        }
        StringAssert.Contains(Assert.ThrowsExactly<IrVerificationException>(() =>
            Lower(Program("GameBanking.ReadBufferByte(0x800);"))).Message, "invalid bank buffer read");
        var backendWithoutMapper = Sm83Backend.Emit(Lower(Program("GameBanking.CopyToBuffer(2, 0x4000, 0, 1);")));
        Assert.AreEqual("GBB007", backendWithoutMapper.Diagnostics.Single().Id);
        Assert.IsTrue(GameFrontend.Analyze([("banked.cs", Program("GameBanking.ReadBufferByte(1u);"))]).Diagnostics.Any(d => d.Id.StartsWith("CS", StringComparison.Ordinal)));
        var spoof = Program("FakeBanking.CopyToBuffer(2, 0x4000, 0, 1);").Replace("static class Game", "static class FakeBanking { public static void CopyToBuffer(byte b, ushort a, ushort o, ushort l) {} } static class Game", StringComparison.Ordinal);
        Assert.IsTrue(GameFrontend.Analyze([("banked.cs", spoof)]).Success);
        var spoofIr = Lower(spoof);
        Assert.IsFalse(spoofIr.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrBankCopy));
        Assert.IsTrue(spoofIr.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrCall));
    }

    [TestMethod]
    public void IrAndBackendPreserveCopyReadOrderAndRom0HelperContract()
    {
        var module = Lower(Source);
        var operations = module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 4, 7 }, operations.OfType<IrBankCopy>().Select(c => (int)c.BufferOffset).ToArray());
        Assert.IsTrue(operations.OfType<IrBankRead>().Any(r => r.BufferOffset == 7));
        Assert.IsLessThan(Array.IndexOf(operations, operations.OfType<IrBankRead>().First()),
            Array.IndexOf(operations, operations.OfType<IrBankCopy>().Last()));
        var backend = Sm83Backend.Emit(module, profile: Profile());
        Assert.IsTrue(backend.Success, string.Join("\n", backend.Diagnostics));
        Assert.AreEqual(6, backend.Layout!.StackRequired); // startup CALL, helper CALL, PUSH AF
        Assert.IsTrue(backend.Layout.Allocations.All(a => a.Address < 0xD000 || a.Address >= 0xD800));
        Assert.IsGreaterThanOrEqualTo(0xD800, backend.Layout.StackBottom);
        Assert.AreEqual(3, backend.Assembly!.Split("call gb_bank_copy", StringSplitOptions.None).Length - 1);
        Assert.IsFalse(backend.Assembly.Contains("ROMX", StringComparison.Ordinal));
        var helper = RuntimeAssembly.Banking();
        Assert.IsLessThan(helper.IndexOf("ld [$2000], a", StringComparison.Ordinal), helper.IndexOf("push af", StringComparison.Ordinal));
        Assert.IsLessThan(helper.LastIndexOf("ld [$2000], a", StringComparison.Ordinal), helper.IndexOf("pop af", StringComparison.Ordinal));
        StringAssert.Contains(RuntimeAssembly.Create(backend.Layout), "ld [gb_current_bank], a");
        var insufficient = Sm83Backend.Emit(module, new Sm83Limits(StackBytes: 5), Profile());
        Assert.AreEqual("GBB004", insufficient.Diagnostics.Single().Id);
        var intersect = Sm83Backend.Emit(module, new Sm83Limits(StackBytes: 0x900), Profile());
        Assert.AreEqual("GBB003", intersect.Diagnostics.Single().Id);
    }

    [TestMethod]
    public async Task InvalidSpanReturnsDiagnosticWithoutPublishingOrTouchingPriorRom()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "bad.cs");
            var output = Path.Combine(folder, "game.gb");
            await File.WriteAllTextAsync(source, Program("GameBanking.CopyToBuffer(2, 0x4005, 0, 2);"));
            await File.WriteAllTextAsync(output, "original");
            var result = await new RomCompiler().CompileAsync([source], new RomBuildOptions(output, Profile: Profile()));
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Diagnostics.Single(), "GBB007");
            Assert.IsEmpty(result.Artifacts);
            Assert.AreEqual("original", await File.ReadAllTextAsync(output));
            foreach (var body in new[] { "GameMemory.WriteByte(0x2000, 2);",
                "ushort address = 0xC100; GameMemory.WriteByte(address, 2);" })
            {
                await File.WriteAllTextAsync(source, Program(body));
                var unsafeWrite = await new RomCompiler().CompileAsync([source], new RomBuildOptions(output, Profile: Profile()));
                Assert.IsFalse(unsafeWrite.Success);
                StringAssert.Contains(unsafeWrite.Diagnostics.Single(), "GBB007");
                Assert.IsEmpty(unsafeWrite.Artifacts);
                Assert.AreEqual("original", await File.ReadAllTextAsync(output));
            }
            Assert.HasCount(2, Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task RgbdsAndSharpBoyAndPyBoyAcceptEverySupportedCapacity()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        var python = Environment.GetEnvironmentVariable("GB_PYBOY_PYTHON");
        if (string.IsNullOrWhiteSpace(bin) && string.IsNullOrWhiteSpace(python))
            Assert.Inconclusive("Set GB_RGBDS_BIN and GB_PYBOY_PYTHON for real banked ROM integration.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(bin), "GB_RGBDS_BIN required alongside GB_PYBOY_PYTHON");
        Assert.IsFalse(string.IsNullOrWhiteSpace(python), "GB_PYBOY_PYTHON required alongside GB_RGBDS_BIN");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var banks in new[] { 4, 8, 16, 32 })
            {
                var profile = Profile(banks);
                var output = Path.Combine(folder, $"banked{banks}.gb");
                var result = await new RomCompiler().CompileAsync([Fixture("BankedAssets.cs")],
                    new RomBuildOptions(output, "BANKED", bin, Profile: profile));
                Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
                var rom = await File.ReadAllBytesAsync(output);
                Assert.HasCount(banks * 0x4000, rom);
                Assert.AreEqual((byte)1, rom[0x147]);
                Assert.AreEqual((byte)profile.RomSizeCode, rom[0x148]);
                Assert.AreEqual((byte)0, rom[0x149]);
                Assert.AreEqual((byte)0xA0, rom[2 * 0x4000]);
                Assert.AreEqual((byte)0xF0, rom[2 * 0x4000 + 0x3FFF]);
                Assert.AreEqual((byte)0, rom[^1]);
                var symbols = await File.ReadAllTextAsync(result.Artifacts["sym"]);
                var layout = Sm83Backend.Emit(Lower(Source), profile: profile).Layout!;
                Assert.IsEmpty(RomValidator.Validate(rom, symbols, layout, profile));
                StringAssert.Contains(symbols, "02:4000 gb_asset_Data");
                StringAssert.Contains(symbols, "02:7ffe gb_asset_Tail");
                StringAssert.Contains(symbols.ToLowerInvariant(), $"{banks - 1:x2}:7fff gb_capacity_guard");
                Assert.IsTrue(symbols.Split('\n').Any(l => l.Contains(" gb_bank_copy", StringComparison.Ordinal) && l.StartsWith("00:", StringComparison.Ordinal)));
                var edgeFile = Path.Combine(folder, $"edge{banks}.bin");
                await File.WriteAllBytesAsync(edgeFile, [0xAB, 0xCD]);
                var edgeProfile = CartridgeProfile.CreateMbc1(banks,
                    [new("Tail", Fixture("tail.bin"), 2, 0x7FFE), new("Data", Fixture("data.bin"), 2, 0x4000),
                     new("Edge", edgeFile, (byte)(banks - 1), 0x7FFE)]);
                var edgeOutput = Path.Combine(folder, $"edge{banks}.gb");
                var edgeResult = await new RomCompiler().CompileAsync([Fixture("BankedAssets.cs")],
                    new RomBuildOptions(edgeOutput, "BANKED", bin, Profile: edgeProfile));
                Assert.IsTrue(edgeResult.Success, string.Join("\n", edgeResult.Diagnostics));
                var edgeRom = await File.ReadAllBytesAsync(edgeOutput);
                Assert.HasCount(banks * 0x4000, edgeRom);
                Assert.AreEqual((byte)0xAB, edgeRom[^2]);
                Assert.AreEqual((byte)0xCD, edgeRom[^1]);
                var edgeSymbols = await File.ReadAllTextAsync(edgeResult.Artifacts["sym"]);
                Assert.IsFalse(edgeSymbols.Contains("gb_capacity_guard", StringComparison.Ordinal));
                Assert.IsEmpty(RomValidator.Validate(edgeRom, edgeSymbols, layout, edgeProfile));
                var tampered = (byte[])rom.Clone();
                tampered[0x148] ^= 1;
                Assert.IsTrue(RomValidator.Validate(tampered, symbols, layout, profile).Any(d => d.StartsWith("GBR004:", StringComparison.Ordinal)));
                tampered = (byte[])rom.Clone();
                tampered[2 * 0x4000] ^= 1;
                Assert.IsTrue(RomValidator.Validate(tampered, symbols, layout, profile).Any(d => d.StartsWith("GBR008:", StringComparison.Ordinal)));
                var gameboy = new Gameboy();
                gameboy.Load(new Cartridge { Data = rom });
                var steps = 0;
                while (gameboy.Memory.Peek(0xC110) != 0x5A && steps++ < 20000) gameboy.RunStep();
                Assert.IsLessThan(20000, steps, $"bank {banks} sentinel timeout");
                foreach (var (address, value) in new[]
                {
                    (0xD000, 0xA1), (0xD001, 0xA2), (0xD002, 0xA3),
                    (0xD004, 0xA1), (0xD005, 0xA2), (0xD006, 0xA3), (0xD007, 0xF0),
                    (0xC100, 0xA1), (0xC101, 0xA1), (0xC102, 0xF0), (0xC110, 0x5A), (0xC000, 1),
                    (0x4000, (int)rom[0x4000]), (0x4001, (int)rom[0x4001])
                }) Assert.AreEqual((byte)value, gameboy.Memory.Peek((ushort)address), $"bank {banks}, ${address:X4}");
                Console.WriteLine($"RGBDS 0.9.0 / SharpBoy: {banks} banks, sentinel in {steps} instructions");
                if (banks == 4)
                {
                    using var process = new Process();
                    process.StartInfo = new ProcessStartInfo(python!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in new[] { FixturePathValidator(), "--rom", output, "--banked", "--frames", "10" })
                        process.StartInfo.ArgumentList.Add(arg);
                    process.Start();
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { process.Kill(entireProcessTree: true); Assert.Fail("PyBoy timed out after 90 seconds"); }
                    var outputText = await stdout + await stderr;
                    Assert.AreEqual(0, process.ExitCode, outputText);
                    StringAssert.Contains(outputText, "PASS: PyBoy 2.6.0 banked");
                    Console.WriteLine(outputText);
                }
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    private static string FixturePathValidator() => Path.Combine(AppContext.BaseDirectory, "IndependentEmulator", "validate.py");
}
