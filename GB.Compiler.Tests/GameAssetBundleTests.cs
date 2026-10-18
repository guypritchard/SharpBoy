using System.Diagnostics;
using GB.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class GameAssetBundleTests
{
    private static string Source(string body) => "using GB.Sdk; static class G { [GameEntryPoint] static void Main() { " + body + " } }";
    private static MemoryLayout Layout(string body) => Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([("game.cs", Source(body))])).Layout!;

    [TestMethod]
    public void RequiredUnusedPairedSizesAndMapIdsAreValidated()
    {
        var background = Layout("GameDisplay.InitializeBackground();");
        var sprite = Layout("GameSprites.Initialize();");
        var neither = Layout("GameDisplay.SetScroll(1, 2);");
        var both = Layout("GameDisplay.InitializeBackground(); GameSprites.Initialize();");
        Assert.IsFalse(neither.UsesBackground);
        Assert.IsFalse(neither.UsesSpriteInit);
        Assert.IsEmpty(new GameAssetBundle().Validate(neither));
        Assert.IsEmpty(SampleAssets.Background.Validate(background));
        Assert.IsEmpty(SampleAssets.Sprite.Validate(sprite));
        Assert.IsEmpty(SampleAssets.All.Validate(both));
        foreach (var (bundle, layout, message) in new (GameAssetBundle, MemoryLayout, string)[]
        {
            (new(), background, "InitializeBackground requires"),
            (new(), sprite, "GameSprites.Initialize requires"),
            (new(SampleAssets.BackgroundTiles), background, "supplied together"),
            (new(backgroundMap: SampleAssets.BackgroundMap), background, "supplied together"),
            (SampleAssets.Background, neither, "unused"),
            (SampleAssets.Sprite, neither, "unused"),
            (new(new byte[63], SampleAssets.BackgroundMap), background, "exactly 64"),
            (new(new byte[65], SampleAssets.BackgroundMap), background, "exactly 64"),
            (new(SampleAssets.BackgroundTiles, new byte[1023]), background, "exactly 1024"),
            (new(SampleAssets.BackgroundTiles, new byte[1025]), background, "exactly 1024"),
            (new(spriteTiles: []), sprite, "exactly 16"),
            (new(spriteTiles: new byte[17]), sprite, "exactly 16")
        })
        {
            var issues = bundle.Validate(layout);
            Assert.IsTrue(issues.Any(i => i.Contains(message, StringComparison.Ordinal)), string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.StartsWith("GBC009:", StringComparison.Ordinal)));
        }
        foreach (var invalid in new byte[] { 4, 255 })
        {
            var map = new byte[1024];
            map[1023] = invalid;
            StringAssert.Contains(new GameAssetBundle(new byte[64], map).Validate(background).Single(), $"entry 1023 references tile {invalid}");
        }
        Assert.IsEmpty(new GameAssetBundle(new byte[64], new byte[1024]).Validate(background)); // ID 0 is valid
    }

    [TestMethod]
    public void BundleDefensivelyCopiesEveryInput()
    {
        var tiles = SampleAssets.BackgroundTiles;
        var map = SampleAssets.BackgroundMap;
        var sprite = SampleAssets.SpriteTiles;
        var bundle = new GameAssetBundle(tiles, map, sprite);
        Array.Fill<byte>(tiles, 255);
        Array.Fill<byte>(map, 255);
        Array.Fill<byte>(sprite, 255);
        CollectionAssert.AreEqual(SampleAssets.BackgroundTiles, bundle.BackgroundTiles.ToArray());
        CollectionAssert.AreEqual(SampleAssets.BackgroundMap, bundle.BackgroundMap.ToArray());
        CollectionAssert.AreEqual(SampleAssets.SpriteTiles, bundle.SpriteTiles.ToArray());
        var returned = bundle.SpriteTiles.ToArray();
        returned[0] ^= 255;
        Assert.AreEqual(SampleAssets.SpriteTiles[0], bundle.SpriteTiles[0]);
    }

    [TestMethod]
    public async Task InvalidAssetsFailBeforeToolsAndPreservePriorArtifacts()
    {
        var folder = NewFolder();
        try
        {
            var source = Path.Combine(folder, "game.cs");
            var output = Path.Combine(folder, "game.gb");
            await File.WriteAllTextAsync(source, Source("GameDisplay.InitializeBackground();"));
            await File.WriteAllTextAsync(output, "previous ROM");
            var result = await new RomCompiler().CompileAsync([source], new(output, RgbdsBinDirectory: "missing-tools"));
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Diagnostics.All(d => d.StartsWith("GBC009:", StringComparison.Ordinal)));
            Assert.IsEmpty(result.Artifacts);
            Assert.AreEqual("previous ROM", await File.ReadAllTextAsync(output));
            Assert.IsEmpty(Directory.GetDirectories(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task RealRgbdsAcceptsAssetFreeOperationsAndCallerDefinedArt()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN for ROM integration.");
        var folder = NewFolder();
        try
        {
            var source = Path.Combine(folder, "game.cs");
            var output = Path.Combine(folder, "game.gb");
            // Deliberately not sample artwork: the compiler must emit exactly caller-supplied data.
            var tiles = Enumerable.Range(0, 64).Select(i => (byte)(i * 3)).ToArray();
            var map = Enumerable.Range(0, 1024).Select(i => (byte)(i % 4)).ToArray();
            var sprite = Enumerable.Range(0, 16).Select(i => (byte)(255 - i)).ToArray();
            foreach (var (body, assets) in new (string, GameAssetBundle)[]
            {
                ("GameDisplay.SetScroll(2, 3);", new()),
                ("GameDisplay.WaitForNextVBlank();", new()),
                ("GameSprites.Set(0, 8, 16, 0); GameSprites.Hide(0);", new()),
                ("GameSprites.Initialize();", new(spriteTiles: sprite)),
                ("GameDisplay.InitializeBackground();", new(tiles, map)),
                ("GameDisplay.InitializeBackground(); GameSprites.Initialize();", new(tiles, map, sprite))
            })
            {
                await File.WriteAllTextAsync(source, Source(body));
                var result = await new RomCompiler().CompileAsync([source], new(output, RgbdsBinDirectory: bin, Assets: assets));
                Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
                var rom = await File.ReadAllBytesAsync(output);
                var sym = await File.ReadAllTextAsync(result.Artifacts["sym"]);
                var report = await File.ReadAllTextAsync(result.Artifacts["memory"]);
                var layout = Layout(body);
                Assert.IsEmpty(RomValidator.Validate(rom, sym, layout, assets: assets));
                Assert.AreEqual(layout.UsesBackground, sym.Contains(" gb_bg_tiles", StringComparison.Ordinal));
                Assert.AreEqual(layout.UsesSpriteInit, sym.Contains(" gb_sprite_tile", StringComparison.Ordinal));
                Assert.AreEqual(layout.UsesBackground, sym.Contains(" gb_display_init", StringComparison.Ordinal));
                Assert.AreEqual(layout.UsesSpriteInit, sym.Contains(" gb_sprite_init", StringComparison.Ordinal));
                foreach (var (name, address, bytes) in new[]
                {
                    ("background tiles", 0x4000, assets.BackgroundTiles),
                    ("background map", 0x4040, assets.BackgroundMap),
                    ("sprite tiles", assets.SpriteAddress, assets.SpriteTiles)
                })
                {
                    if (bytes.IsDefault) continue;
                    CollectionAssert.AreEqual(bytes.ToArray(), rom.Skip(address).Take(bytes.Length).ToArray());
                    StringAssert.Contains(report, $"display asset {name} bank 1 ${address:X4} size {bytes.Length} SHA-256 {SampleAssets.Hash(bytes.ToArray())}");
                    var damaged = (byte[])rom.Clone();
                    damaged[address] ^= 1;
                    Assert.IsTrue(RomValidator.Validate(damaged, sym, layout, assets: assets).Any(d => d.StartsWith("GBR008:", StringComparison.Ordinal)));
                }
                if (layout.UsesSpriteInit)
                {
                    Assert.AreEqual(layout.UsesBackground ? 0x4440 : 0x4000, assets.SpriteAddress);
                    StringAssert.Contains(sym, $"01:{assets.SpriteAddress:x4} gb_sprite_tile");
                    var wrongBank = sym.Replace($"01:{assets.SpriteAddress:x4} gb_sprite_tile", $"02:{assets.SpriteAddress:x4} gb_sprite_tile", StringComparison.Ordinal);
                    Assert.IsTrue(RomValidator.Validate(rom, wrongBank, layout, assets: assets).Any(d => d.StartsWith("GBR007:", StringComparison.Ordinal)));
                }
                else if (!layout.UsesBackground) Assert.IsFalse(report.Contains("display asset", StringComparison.Ordinal));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task CliAcceptsExplicitFilesAndDiagnosesBadInputs()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN for CLI integration.");
        var folder = NewFolder();
        try
        {
            foreach (var option in new[] { "--background-tiles", "--background-map", "--sprite-tiles" })
            {
                foreach (var tail in new[] { Array.Empty<string>(), new[] { "--out", "game.gb" } })
                {
                    var missing = await Cli([option, .. tail]);
                    Assert.AreEqual(2, missing.Code, missing.Text);
                    StringAssert.Contains(missing.Text, $"GBC003: {option} requires a FILE");
                }
                foreach (var path in new[] { Path.Combine(folder, "missing.bin"), folder })
                {
                    var unreadable = await Cli([option, path]);
                    Assert.AreEqual(2, unreadable.Code, unreadable.Text);
                    StringAssert.Contains(unreadable.Text, $"GBC001: Cannot read {option} file");
                }
            }
            var source = Path.Combine(folder, "game.cs");
            var output = Path.Combine(folder, "game.gb");
            await File.WriteAllTextAsync(source, Source("GameDisplay.InitializeBackground(); GameSprites.Initialize();"));
            string[] common = ["--out", output, "--rgbds-bin", bin, source];
            var absent = await Cli(common);
            Assert.AreEqual(1, absent.Code, absent.Text);
            StringAssert.Contains(absent.Text, "GBC009:");
            var result = await Cli([.. common, "--background-tiles", SampleAssets.PathFor("background.2bpp"),
                "--background-map", SampleAssets.PathFor("background.tilemap"), "--sprite-tiles", SampleAssets.PathFor("sprite.2bpp")]);
            Assert.AreEqual(0, result.Code, result.Text);
            var rom = await File.ReadAllBytesAsync(output);
            CollectionAssert.AreEqual(SampleAssets.BackgroundTiles, rom.Skip(0x4000).Take(64).ToArray());
            CollectionAssert.AreEqual(SampleAssets.BackgroundMap, rom.Skip(0x4040).Take(1024).ToArray());
            CollectionAssert.AreEqual(SampleAssets.SpriteTiles, rom.Skip(0x4440).Take(16).ToArray());
            var bad = Path.Combine(folder, "bad sprite.bin");
            await File.WriteAllBytesAsync(bad, [0]);
            var invalid = await Cli([.. common, "--background-tiles", SampleAssets.PathFor("background.2bpp"),
                "--background-map", SampleAssets.PathFor("background.tilemap"), "--sprite-tiles", bad]);
            Assert.AreEqual(1, invalid.Code, invalid.Text);
            StringAssert.Contains(invalid.Text, "exactly 16 bytes");
            CollectionAssert.AreEqual(rom, await File.ReadAllBytesAsync(output));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-display-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task<(int Code, string Text)> Cli(string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.Combine(AppContext.BaseDirectory, "GB.Compiler.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(AppContext.BaseDirectory, "GB.Compiler.Tests.deps.json"),
            Path.Combine(AppContext.BaseDirectory, "GB.Compiler.Cli.dll") }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start CLI");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }
}
