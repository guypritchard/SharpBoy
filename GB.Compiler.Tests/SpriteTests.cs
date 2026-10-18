using System.Diagnostics;
using GB.Compiler;
using GB.Emulator.Core;
using GB.Emulator.Core.Rendering;
using GB.Sdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class SpriteTests
{
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sprites.cs");
    private static IrModule Lower(string source) => GameIrCompiler.AnalyzeAndLower([("sprite.cs", source)]);

    [TestMethod]
    public void IdentityConstantsAndIrOrder()
    {
        const string source = "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameSprites.Initialize(); byte x = 20; GameSprites.Set(39, x, 16, 0xF0); GameSprites.Hide(39); } }";
        var module = Lower(source);
        CollectionAssert.AreEqual(new[] { IrSpriteKind.Initialize, IrSpriteKind.Set, IrSpriteKind.Hide },
            module.Functions.Single().Blocks.Single().Instructions.OfType<IrSprite>().Select(s => s.Kind).ToArray());
        StringAssert.Contains(IrFormatter.Format(module), "intrinsic.sprite.set observable index 39 attributes $F0");
        var backend = Sm83Backend.Emit(module);
        Assert.IsTrue(backend.Success, string.Join("\n", backend.Diagnostics));
        Assert.AreEqual(6, backend.Layout!.StackRequired);
        Assert.IsTrue(backend.Layout.UsesSprites);
        var asm = backend.Assembly!;
        Assert.IsLessThan(asm.IndexOf("call gb_wait_oam_safe", StringComparison.Ordinal),
            asm.IndexOf("call gb_sprite_init", StringComparison.Ordinal));
        StringAssert.Contains(asm, "ld hl, $FE9C");
        StringAssert.Contains(asm, "ld a, $04");
        var runtime = RuntimeAssembly.Create(backend.Layout);
        StringAssert.Contains(runtime, "gb_wait_oam_safe::");
        StringAssert.Contains(runtime, "cp 153");
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameSprites.Initialize());
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameSprites.Set(0, 8, 16, 0));
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameSprites.Hide(0));
        const string spoof = "namespace GB.Sdk { public static class GameSprites { public static void Hide(byte i) {} } } static class G { [GB.Sdk.GameEntryPoint] static void Main() { GB.Sdk.GameSprites.Hide(2); } }";
        var fake = Lower(spoof);
        Assert.IsFalse(fake.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrSprite));
        Assert.IsTrue(fake.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrCall));
        foreach (var statement in new[] { "GameSprites.Hide(40);", "GameSprites.Set(0, 8, 16, 1);",
            "byte i = 0; GameSprites.Hide(i);", "byte a = 0; GameSprites.Set(0, 8, 16, a);" })
            Assert.IsTrue(GameFrontend.Analyze([("bad.cs", "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { " + statement + " } }")])
                .Diagnostics.Any(d => d.Id == "GBL010"), statement);
    }

    [TestMethod]
    public void MalformedIrFailsClosed()
    {
        var module = Lower("using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameSprites.Set(0, 8, 16, 0); } }");
        var block = module.Functions.Single().Blocks.Single();
        var at = block.Instructions.FindIndex(i => i is IrSprite);
        var original = (IrSprite)block.Instructions[at];
        foreach (var bad in new[] { original with { Index = 40 }, original with { Attributes = 1 },
            original with { Kind = (IrSpriteKind)99 }, original with { Coordinates = [] },
            original with { Coordinates = [new IrValue("fake", IrType.U8), original.Coordinates[1]] } })
        {
            block.Instructions[at] = bad;
            Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
            Assert.AreEqual("GBB001", Sm83Backend.Emit(module).Diagnostics.Single().Id);
        }
    }

    [TestMethod]
    public void SpriteTileHasStable2BppHash()
    {
        Assert.HasCount(16, SampleAssets.SpriteTiles);
        Assert.AreEqual("ecad92431cabb0bb47965254ea8b0039b30794a032828c492d872ca89cc57d7d", SampleAssets.SpriteHash);
        Assert.AreEqual("50682b8a4bbdcb4a6cd262755ac5626cee38ca8f5347c88025031982eeae0ab4", SampleAssets.BackgroundHash);
        CollectionAssert.AreEqual(new byte[] { 0x18, 0x18, 0x3C, 0x3C }, SampleAssets.SpriteTiles.Take(4).ToArray());
    }

    [TestMethod]
    public async Task LcdOffInitializationAndHideAreSafe()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN for ROM integration.");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-obj-off-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "off.cs");
            await File.WriteAllTextAsync(input, "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameSprites.Initialize(); GameSprites.Set(39, 81, 52, 0xF0); GameSprites.Hide(39); GameMemory.WriteByte(0xC200, 0xA5); } }");
            var result = await new RomCompiler().CompileAsync([input], new RomBuildOptions(Path.Combine(folder, "off.gb"), RgbdsBinDirectory: bin, Assets: SampleAssets.Sprite));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var gb = new Gameboy();
            gb.Load(new Cartridge { Data = await File.ReadAllBytesAsync(result.Artifacts["gb"]) });
            // Force LCD off while retaining unusual display configuration bits.
            gb.Memory.Write8(0x11, 0xFF40);
            for (ushort a = 0xFE00; a < 0xFEA0; a++) gb.Memory.Write8(0xBB, a);
            var steps = 0;
            while (gb.Memory.Peek(0xC200) != 0xA5 && steps++ < 10000) gb.RunStep();
            Assert.IsLessThan(10000, steps);
            Assert.AreEqual((byte)0x13, gb.Memory.Peek(0xFF40));
            Assert.AreEqual((byte)0, gb.Memory.Peek(0xFE9C));
            Assert.AreEqual((byte)81, gb.Memory.Peek(0xFE9D));
            Assert.AreEqual((byte)4, gb.Memory.Peek(0xFE9E));
            Assert.AreEqual((byte)0xF0, gb.Memory.Peek(0xFE9F));
            Assert.AreEqual((byte)0xE4, gb.Memory.Peek(0xFF48));
            Assert.AreEqual((byte)0xE4, gb.Memory.Peek(0xFF49));
            for (ushort a = 0xFE00; a < 0xFE9C; a++) Assert.AreEqual((byte)0, gb.Memory.Peek(a));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task Mbc1BankCopyStillRestoresBankOneForSprites()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN for ROM integration.");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-obj-mbc1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var binary = Path.Combine(folder, "asset.bin");
            await File.WriteAllBytesAsync(binary, [0xA1, 0xA2, 0xA3]);
            var input = Path.Combine(folder, "banked.cs");
            await File.WriteAllTextAsync(input, "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameBanking.CopyToBuffer(2, 0x4000, 0, 3); GameDisplay.InitializeBackground(); GameSprites.Initialize(); GameSprites.Set(0, 32, 56, 0); GameMemory.WriteByte(0xC200, GameBanking.ReadBufferByte(0)); } }");
            var profile = CartridgeProfile.CreateMbc1(4, [new BankedAsset("Data", binary, 2, 0x4000)]);
            var output = Path.Combine(folder, "banked.gb");
            var result = await new RomCompiler().CompileAsync([input], new RomBuildOptions(output, RgbdsBinDirectory: bin, Profile: profile, Assets: SampleAssets.All));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var rom = await File.ReadAllBytesAsync(output);
            var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([(input, await File.ReadAllTextAsync(input))]), profile: profile).Layout!;
            Assert.IsEmpty(RomValidator.Validate(rom, await File.ReadAllTextAsync(result.Artifacts["sym"]), layout, profile, SampleAssets.All));
            var gb = new Gameboy();
            gb.Load(new Cartridge { Data = rom });
            var steps = 0;
            while (gb.Memory.Peek(0xC200) != 0xA1 && steps++ < 150000) gb.RunStep();
            Assert.IsLessThan(150000, steps);
            Assert.AreEqual((byte)1, gb.Memory.Peek(0xC000));
            Assert.AreEqual((byte)0xA1, gb.Memory.Peek(0xD000));
            Assert.AreEqual((byte)0x93, gb.Memory.Peek(0xFF40));
            Assert.AreEqual((byte)4, gb.Memory.Peek(0xFE02));
            for (int i = 0; i < SampleAssets.SpriteTiles.Length; i++)
                Assert.AreEqual(SampleAssets.SpriteTiles[i], gb.Memory.Peek((ushort)(0x8040 + i)));
            for (int i = 0; i < SampleAssets.BackgroundTiles.Length; i++)
                Assert.AreEqual(SampleAssets.BackgroundTiles[i], gb.Memory.Peek((ushort)(0x8000 + i)));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task RealRgbdsSharpBoyAndPyBoySprite()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        var python = Environment.GetEnvironmentVariable("GB_PYBOY_PYTHON");
        if (string.IsNullOrWhiteSpace(bin) || string.IsNullOrWhiteSpace(python))
            Assert.Inconclusive("Set GB_RGBDS_BIN and GB_PYBOY_PYTHON for real integration.");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-sprites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "sprites.gb");
            var result = await new RomCompiler().CompileAsync([Sample], new RomBuildOptions(output, "SPRITES", bin, Assets: SampleAssets.All));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var rom = await File.ReadAllBytesAsync(output);
            var sym = await File.ReadAllTextAsync(result.Artifacts["sym"]);
            var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([(Sample, await File.ReadAllTextAsync(Sample))])).Layout!;
            Assert.IsEmpty(RomValidator.Validate(rom, sym, layout, assets: SampleAssets.All));
            foreach (var helper in new[] { "gb_sprite_init", "gb_wait_oam_safe" })
            {
                var broken = System.Text.RegularExpressions.Regex.Replace(sym,
                    $@"(?m)^(00:[0-9a-fA-F]{{4}} {helper})(\r?)$", "$1_missing$2");
                Assert.AreNotEqual(sym, broken);
                Assert.IsTrue(RomValidator.Validate(rom, broken, layout, assets: SampleAssets.All)
                    .Any(issue => issue.Contains($"native OBJ helper {helper}", StringComparison.Ordinal)));
            }
            CollectionAssert.AreEqual(SampleAssets.SpriteTiles, rom.Skip(0x4440).Take(16).ToArray());
            var damaged = (byte[])rom.Clone();
            damaged[0x4440] ^= 1;
            Assert.IsTrue(RomValidator.Validate(damaged, sym, layout, assets: SampleAssets.All).Any(d => d.StartsWith("GBR008:", StringComparison.Ordinal)));
            foreach (var initiallyOn in new[] { false, true })
            {
                var gb = new Gameboy();
                gb.Load(new Cartridge { Data = rom });
                if (initiallyOn) gb.Memory.Write8(0x91, 0xFF40);
                for (ushort a = 0xFE00; a < 0xFEA0; a++) gb.Memory.Write8(0xDD, a);
                var lastLcdc = gb.Memory.Peek(0xFF40);
                var disableAt = -1;
                var changes = new List<(long Cycles, byte Ly, byte X)>();
                byte previousX = 0;
                for (var steps = 0; steps < 800000 && changes.Count < 4; steps++)
                {
                    var ly = gb.Scanline;
                    gb.RunStep();
                    var lcdc = gb.Memory.Peek(0xFF40);
                    if (initiallyOn && lastLcdc != 0 && lcdc == 0)
                    {
                        Assert.IsGreaterThanOrEqualTo(144, (int)ly);
                        Assert.AreNotEqual((byte)153, ly, "Sprite initialization must defer final-line VBlank entry");
                        disableAt = steps;
                    }
                    lastLcdc = lcdc;
                    var x = gb.Memory.Peek(0xFE01);
                    if (x != previousX && x is >= 32 and <= 36)
                    {
                        changes.Add((gb.EmulatedCycles, gb.Scanline, x));
                        previousX = x;
                    }
                }
                Assert.HasCount(4, changes);
                if (initiallyOn) Assert.IsGreaterThanOrEqualTo(0, disableAt);
                Assert.AreEqual((byte)0x93, gb.Memory.Peek(0xFF40));
                Assert.AreEqual((byte)0xE4, gb.Memory.Peek(0xFF48));
                Assert.AreEqual((byte)0xE4, gb.Memory.Peek(0xFF49));
                for (var i = 0; i < 16; i++) Assert.AreEqual(SampleAssets.SpriteTiles[i], gb.Memory.Peek((ushort)(0x8040 + i)));
                Assert.AreEqual((byte)56, gb.Memory.Peek(0xFE00));
                Assert.AreEqual((byte)4, gb.Memory.Peek(0xFE02));
                Assert.AreEqual((byte)0, gb.Memory.Peek(0xFE03));
                for (ushort a = 0xFE04; a < 0xFEA0; a++) Assert.AreEqual((byte)0, gb.Memory.Peek(a), $"OAM {a:X4}");
                for (var i = 1; i < changes.Count; i++)
                {
                    Assert.AreEqual((byte)(32 + i), changes[i].X);
                    Assert.IsGreaterThanOrEqualTo(144, (int)changes[i].Ly);
                    Assert.AreNotEqual((byte)153, changes[i].Ly, "OAM writes must defer LY 153 to the next VBlank");
                    Assert.IsTrue(changes[i].Cycles - changes[i - 1].Cycles is >= 69000 and <= 71500);
                }
                var withSprite = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
                DmgScreenRenderer.RenderScreen(gb.CaptureVideoState(), withSprite);
                gb.Memory.Write8(0, 0xFE00);
                var withoutSprite = new byte[withSprite.Length];
                DmgScreenRenderer.RenderScreen(gb.CaptureVideoState(), withoutSprite);
                var visibleX = changes[^1].X - 8;
                Assert.IsTrue(Enumerable.Range(0, 8).SelectMany(y => Enumerable.Range(0, 8)
                    .Select(x => (Y: 40 + y, X: visibleX + x)))
                    .Any(pixel => withSprite[pixel.Y * DmgScreenRenderer.Width + pixel.X] == 3 &&
                        withSprite[pixel.Y * DmgScreenRenderer.Width + pixel.X] !=
                        withoutSprite[pixel.Y * DmgScreenRenderer.Width + pixel.X]),
                    "At least one black OBJ pixel must differ from the background");
            }
            var start = new ProcessStartInfo(python) { UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "IndependentEmulator", "validate.py"),
                "--rom", output, "--sprites", "--frames", "18" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Python");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); Assert.Fail("PyBoy sprites timeout"); }
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            Assert.AreEqual(0, process.ExitCode, stdout + stderr);
            StringAssert.Contains(stdout, "PASS: PyBoy 2.6.0 sprites");
            StringAssert.Contains(stdout, "rendered OBJ pixel");
        }
        finally { Directory.Delete(folder, true); }
    }
}
