using GB.Compiler;
using GB.Emulator.Core;
using GB.Sdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class DisplayTests
{
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScrollingBackground.cs");

    [TestMethod]
    public void ExactSdkIdentityOrderedIrAndBackend()
    {
        const string source = "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameDisplay.InitializeBackground(); GameMemory.WriteByte(0xC100, 1); GameDisplay.WaitForNextVBlank(); GameDisplay.SetScroll(2, 3); } }";
        var module = GameIrCompiler.AnalyzeAndLower([("real.cs", source)]);
        var instructions = module.Functions.Single().Blocks.Single().Instructions;
        CollectionAssert.AreEqual(new[] { IrDisplayKind.InitializeBackground, IrDisplayKind.WaitForNextVBlank, IrDisplayKind.SetScroll },
            instructions.OfType<IrDisplay>().Select(i => i.Kind).ToArray());
        Assert.IsGreaterThan(instructions.FindIndex(i => i is IrDisplay { Kind: IrDisplayKind.InitializeBackground }), instructions.FindIndex(i => i is IrWriteByte));
        Assert.IsLessThan(instructions.FindIndex(i => i is IrDisplay { Kind: IrDisplayKind.WaitForNextVBlank }), instructions.FindIndex(i => i is IrWriteByte));
        Assert.IsTrue(instructions.OfType<IrDisplay>().All(i => i.Effect == IrEffect.HardwareReadWrite));
        var dump = IrFormatter.Format(module);
        StringAssert.Contains(dump, "intrinsic.display.waitfornextvblank observable");
        var emitted = Sm83Backend.Emit(module);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.AreEqual(4, emitted.Layout!.StackRequired); // startup -> entry -> native helper
        Assert.IsTrue(emitted.Layout.UsesDisplay);
        var asm = emitted.Assembly!;
        var init = asm.IndexOf("call gb_display_init", StringComparison.Ordinal);
        var write = asm.IndexOf("ld [hl], a", StringComparison.Ordinal);
        var wait = asm.IndexOf("call gb_wait_next_vblank", StringComparison.Ordinal);
        var scrollX = asm.IndexOf("ldh [$FF43], a", StringComparison.Ordinal);
        var scrollY = asm.IndexOf("ldh [$FF42], a", StringComparison.Ordinal);
        Assert.IsTrue(init < write && write < wait && wait < scrollX && scrollX < scrollY);
        StringAssert.Contains(RuntimeAssembly.Create(emitted.Layout), "gb_display_init::");
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameDisplay.InitializeBackground());
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameDisplay.WaitForNextVBlank());
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameDisplay.SetScroll(1, 2));

        // A same-namespace source method is a normal game call, NOT an SDK native operation.
        const string spoof = "namespace GB.Sdk { public static class GameDisplay { public static void SetScroll(byte x, byte y) {} } } static class G { [GB.Sdk.GameEntryPoint] static void Main() { GB.Sdk.GameDisplay.SetScroll(2, 3); } }";
        var lookalike = GameIrCompiler.AnalyzeAndLower([("spoof.cs", spoof)]);
        Assert.IsFalse(lookalike.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrDisplay));
        Assert.IsTrue(lookalike.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrCall));
        const string wrongSignature = "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameDisplay.SetScroll(1); } }";
        Assert.IsFalse(GameFrontend.Analyze([("invalid.cs", wrongSignature)]).Success);
        const string external = "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { System.Console.WriteLine(\"SetScroll\"); } }";
        Assert.IsTrue(GameFrontend.Analyze([("external.cs", external)]).Diagnostics.Any(d => d.Id == "GBL008"));
    }

    [TestMethod]
    public void MalformedDisplayIrIsRejected()
    {
        var module = GameIrCompiler.AnalyzeAndLower([("ir.cs", "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { GameDisplay.WaitForNextVBlank(); } }")]);
        var block = module.Functions.Single().Blocks.Single();
        var index = block.Instructions.FindIndex(i => i is IrDisplay);
        var original = (IrDisplay)block.Instructions[index];
        foreach (var invalid in new[] { original with { Kind = (IrDisplayKind)42 },
            original with { Kind = IrDisplayKind.SetScroll },
            original with { Arguments = [new IrValue("forged", IrType.U8)] } })
        {
            block.Instructions[index] = invalid;
            Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
            Assert.AreEqual("GBB001", Sm83Backend.Emit(module).Diagnostics.Single().Id);
        }
    }

    [TestMethod]
    public void Deterministic2BppAssetsAndMap()
    {
        Assert.HasCount(64, SampleAssets.BackgroundTiles);
        Assert.HasCount(1024, SampleAssets.BackgroundMap);
        Assert.AreEqual("50682b8a4bbdcb4a6cd262755ac5626cee38ca8f5347c88025031982eeae0ab4", SampleAssets.BackgroundHash);
        Assert.IsTrue(SampleAssets.BackgroundTiles.Take(16).All(b => b == 0));
        Assert.AreEqual(0xFF, SampleAssets.BackgroundTiles[16]); // bar row: color index 1
        Assert.AreEqual(0x00, SampleAssets.BackgroundTiles[17]);
        Assert.AreEqual(0x00, SampleAssets.BackgroundTiles[20]); // bar row 2: color index 2
        Assert.AreEqual(0xFF, SampleAssets.BackgroundTiles[21]);
        CollectionAssert.AreEqual(new byte[] { 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3 }, SampleAssets.BackgroundMap.Take(12).ToArray());
        Assert.AreEqual((byte)2, SampleAssets.BackgroundMap[4 * 32]);
        Assert.IsTrue(SampleAssets.BackgroundMap.All(t => t is >= 1 and <= 3));
    }

    [TestMethod]
    public void AllExecutableAssemblyResumesAnchoredRom0Section()
    {
        var module = GameIrCompiler.AnalyzeAndLower([(Sample, File.ReadAllText(Sample))]);
        var backend = Sm83Backend.Emit(module);
        Assert.IsTrue(backend.Success, string.Join("\n", backend.Diagnostics));
        // Normalize platform line endings before asserting directive order. The generated
        // source uses Environment.NewLine, while RGBDS directives are line-oriented.
        var assembly = (RuntimeAssembly.Create(backend.Layout!) + backend.Assembly).ReplaceLineEndings("\n");
        var startup = assembly.IndexOf("SECTION \"GB startup\", ROM0[$0150]", StringComparison.Ordinal);
        var display = assembly.IndexOf("gb_wait_next_vblank::", StringComparison.Ordinal);
        var saved = assembly.IndexOf("\nPUSHS\n", StringComparison.Ordinal);
        var restored = assembly.IndexOf("\nPOPS ;", StringComparison.Ordinal);
        var generated = assembly.IndexOf("gb_entry:", StringComparison.Ordinal);
        Assert.IsTrue(startup >= 0 && startup < display && display < saved && saved < restored && restored < generated);
        Assert.IsFalse(assembly.Contains("SECTION \"GB LCD helpers\"", StringComparison.Ordinal));
        Assert.IsTrue(assembly.Contains("SECTION \"GB cartridge header\", ROM0[$0100]", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RealRgbdsBackgroundAndFullNextVBlankUnderSharpBoy()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN to RGBDS 0.9.0 for ROM integration.");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-display-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = await new RomCompiler().CompileAsync([Sample],
                new RomBuildOptions(Path.Combine(folder, "scroll.gb"), "SCROLLING", bin, Assets: SampleAssets.Background));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            Assert.HasCount(5, result.Artifacts);
            var rom = await File.ReadAllBytesAsync(result.Artifacts["gb"]);
            var sym = await File.ReadAllTextAsync(result.Artifacts["sym"]);
            var map = await File.ReadAllTextAsync(result.Artifacts["map"]);
            var asm = await File.ReadAllTextAsync(result.Artifacts["asm"]);
            var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([(Sample, await File.ReadAllTextAsync(Sample))])).Layout!;
            Assert.IsTrue(layout.UsesDisplay);
            Assert.HasCount(0x8000, rom);
            Assert.IsEmpty(RomValidator.Validate(rom, sym, layout, assets: SampleAssets.Background));
            StringAssert.Contains(map, "SECTION: $0150-");
            Assert.IsFalse(map.Contains("[\"GB LCD helpers\"]", StringComparison.Ordinal));
            var romSections = map.Split("ROM0 bank #0:", StringSplitOptions.None)[1]
                .Split("ROMX bank #1:", StringSplitOptions.None)[0]
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("SECTION:", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(8, romSections); // reset, five vectors, header, anchored startup/helpers/generated code
            Assert.IsTrue(romSections.Take(7).All(line =>
                System.Text.RegularExpressions.Regex.IsMatch(line, @"SECTION: \$(?:0000|0040|0048|0050|0058|0060|0100)-")));
            StringAssert.Contains(romSections[7], "SECTION: $0150-");
            StringAssert.Contains(asm, "SECTION \"GB startup\", ROM0[$0150]");
            StringAssert.Contains(asm, "PUSHS");
            StringAssert.Contains(asm, "POPS");
            Assert.AreEqual((byte)0xFF, rom[0x4010]);
            Assert.IsTrue(sym.Contains("gb_display_init", StringComparison.Ordinal));
            // RGBDS local labels are scoped under distinct parents and must not be
            // mistaken for duplicate global definitions by the ROM validator.
            var locals = sym + "\n00:0174 gb_display_init.loop\n00:0180 gb_wait_next_vblank.loop\n";
            Assert.IsEmpty(RomValidator.Validate(rom, locals, layout, assets: SampleAssets.Background));
            Assert.IsTrue(RomValidator.Validate(rom, locals + "00:0190 gb_display_init.loop\n", layout, assets: SampleAssets.Background)
                .Any(d => d.Contains("duplicate linker symbol gb_display_init.loop", StringComparison.Ordinal)));
            Assert.IsTrue(RomValidator.Validate(rom, sym + "\n00:0200 gb_display_init\n", layout, assets: SampleAssets.Background)
                .Any(d => d.Contains("duplicate linker symbol gb_display_init", StringComparison.Ordinal)));
            var withoutHelper = System.Text.RegularExpressions.Regex.Replace(sym,
                @"(?m)^(00:[0-9a-fA-F]{4} gb_display_init)(\r?)$", "$1.missing$2");
            Assert.AreNotEqual(sym, withoutHelper);
            Assert.IsTrue(RomValidator.Validate(rom, withoutHelper, layout, assets: SampleAssets.Background)
                .Any(d => d.Contains("native LCD helper gb_display_init", StringComparison.Ordinal)));
            Assert.IsTrue(RomValidator.Validate(rom, withoutHelper + "\n00:0063 gb_display_init\n", layout, assets: SampleAssets.Background)
                .Any(d => d.Contains("native LCD helper gb_display_init", StringComparison.Ordinal)));
            var bad = (byte[])rom.Clone();
            bad[0x4000] ^= 1;
            Assert.IsTrue(RomValidator.Validate(bad, sym, layout, assets: SampleAssets.Background).Any(d => d.StartsWith("GBR008:", StringComparison.Ordinal)));

            // Each machine owns its CPU state; these fixtures also use shared toolchain resources.
            foreach (var preEnabled in new[] { false, true })
            {
                var gb = new Gameboy();
                gb.Load(new Cartridge { Data = rom });
                foreach (var address in new[] { 0x8000, 0x8010, 0x803f, 0x9800, 0x9bff })
                    gb.Memory.Write8(0xA5, (ushort)address);
                if (preEnabled) gb.Memory.Write8(0x91, 0xFF40);
                var scrollEvents = new List<(long Cycles, byte Ly, byte X)>();
                var disabledAt = -1;
                var priorLcdc = gb.Memory.Peek(0xFF40);
                var previousScroll = gb.Memory.Peek(0xFF43);
                const int instructionBudget = 180000;
                int steps;
                for (steps = 0; steps < instructionBudget && scrollEvents.Count < 3; steps++)
                {
                    var priorLy = gb.Scanline;
                    gb.RunStep();
                    var lcdc = gb.Memory.Peek(0xFF40);
                    if (preEnabled && priorLcdc != 0 && lcdc == 0)
                    {
                        Assert.IsGreaterThanOrEqualTo(144, (int)priorLy, "LCD must be disabled only at a safe VBlank point");
                        disabledAt = steps;
                    }
                    priorLcdc = lcdc;
                    var scroll = gb.Memory.Peek(0xFF43);
                    if (scroll != previousScroll)
                    {
                        scrollEvents.Add((gb.EmulatedCycles, gb.Scanline, scroll));
                        previousScroll = scroll;
                    }
                }
                Assert.IsLessThan(instructionBudget, steps, $"Pre-enabled={preEnabled}: only {scrollEvents.Count} scroll updates");
                if (preEnabled) Assert.IsGreaterThanOrEqualTo(0, disabledAt);
                Assert.AreEqual((byte)0x91, gb.Memory.Peek(0xFF40));
                Assert.AreEqual((byte)0xE4, gb.Memory.Peek(0xFF47));
                Assert.AreEqual((byte)3, gb.Memory.Peek(0xFF43));
                // SCY follows SCX by a handful of emitted instructions.
                for (var i = 0; i < 30 && gb.Memory.Peek(0xFF42) != 3; i++) gb.RunStep();
                Assert.AreEqual((byte)3, gb.Memory.Peek(0xFF42));
                Assert.AreEqual((byte)0, gb.Memory.Peek(0xFFFF)); // IE disabled
                var tiles = SampleAssets.BackgroundTiles;
                var tilemap = SampleAssets.BackgroundMap;
                for (int i = 0; i < tiles.Length; i++)
                    Assert.AreEqual(tiles[i], gb.Memory.Peek((ushort)(0x8000 + i)), $"tile byte {i}");
                for (int i = 0; i < tilemap.Length; i++)
                    Assert.AreEqual(tilemap[i], gb.Memory.Peek((ushort)(0x9800 + i)), $"map byte {i}");
                for (int i = 0; i < scrollEvents.Count; i++)
                {
                    Assert.AreEqual((byte)(i + 1), scrollEvents[i].X);
                    Assert.IsGreaterThanOrEqualTo(144, (int)scrollEvents[i].Ly, $"scroll #{i + 1} must occur in VBlank");
                    if (i > 0)
                    {
                        // The sample re-enters WaitForNextVBlank during the current VBlank.
                        // Returning in the same VBlank would yield only a few hundred cycles.
                        var gap = scrollEvents[i].Cycles - scrollEvents[i - 1].Cycles;
                        Assert.IsTrue(gap >= 69000 && gap <= 71500,
                            $"wait entered in VBlank must span a complete 70224-cycle frame; observed {gap} cycles");
                    }
                }
                Console.WriteLine($"SharpBoy LCD initially {(preEnabled ? "on" : "off")}: 3 VBlanks in {steps} instructions, cycles {string.Join(", ", scrollEvents.Select(e => e.Cycles))}");
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
