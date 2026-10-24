using GB.Compiler;
using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class RomPipelineTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ArithmeticGame.cs");

    [TestMethod]
    public async Task FakeProcessFailurePreservesOldOutputAndCleansWorkDirectory()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "game.gb");
            await File.WriteAllTextAsync(output, "old-cartridge");
            var runner = new FakeRunner((tool, args, workingDirectory) =>
            {
                if (args.SequenceEqual(["--version"])) return new(0, Path.GetFileNameWithoutExtension(tool) + " v0.9.0", "");
                Assert.IsTrue(workingDirectory.StartsWith(folder, StringComparison.Ordinal));
                return new(1, "", "bad operand in game.asm:17");
            });
            var result = await new RomCompiler(runner).CompileAsync([Fixture], new RomBuildOptions(output));
            Assert.IsFalse(result.Success);
            StringAssert.Contains(result.Diagnostics.Single(), "GBT003: rgbasm");
            StringAssert.Contains(result.Diagnostics.Single(), "bad operand in game.asm:17");
            Assert.AreEqual("old-cartridge", await File.ReadAllTextAsync(output));
            Assert.IsNull(result.WorkDirectory);
            Assert.IsEmpty(Directory.GetDirectories(folder));
            Assert.HasCount(4, runner.Invocations); // versions before assembly
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task FakeVersionMismatchPreventsAssemblyAndKeepsDiagnosticWork()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var runner = new FakeRunner((tool, args, _) => new(0, Path.GetFileNameWithoutExtension(tool) + " v0.9.1", ""));
            var result = await new RomCompiler(runner).CompileAsync([Fixture],
                new RomBuildOptions(Path.Combine(folder, "game.gb"), RgbdsBinDirectory: folder, KeepIntermediates: true));
            Assert.IsFalse(result.Success);
            Assert.HasCount(3, result.Diagnostics);
            Assert.IsTrue(result.Diagnostics.All(d => d.StartsWith("GBT002:", StringComparison.Ordinal)));
            Assert.HasCount(3, runner.Invocations);
            Assert.IsTrue(runner.Invocations.All(x => x.WorkingDirectory == folder));
            Assert.IsNotNull(result.WorkDirectory);
            Assert.IsTrue(Directory.Exists(result.WorkDirectory));
            Directory.Delete(result.WorkDirectory, true);
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task MidPublicationFailureRestoresAllFivePreviousArtifacts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "game.gb");
            var prior = new Dictionary<string, byte[]>();
            foreach (var suffix in new[] { ".gb", ".asm", ".map", ".sym", ".memory.txt" })
            {
                var path = Path.Combine(folder, "game" + suffix);
                prior[path] = System.Text.Encoding.UTF8.GetBytes("prior-" + suffix + "\0\u00ff");
                await File.WriteAllBytesAsync(path, prior[path]);
            }
            var fs = new FailingFileSystem { FailMoveNumber = 3 };
            var result = await new RomCompiler(SyntheticRunner(), fs).CompileAsync([Fixture], new RomBuildOptions(output));
            Assert.IsFalse(result.Success);
            Assert.IsEmpty(result.Artifacts);
            StringAssert.Contains(result.Diagnostics.Single(), "GBC006: Cannot publish artifacts");
            Assert.AreEqual(3, fs.PublishedMoves);
            foreach (var (path, bytes) in prior)
                CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path), path);
            Assert.IsEmpty(Directory.GetDirectories(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task MidPublicationFailureRemovesNewlyCreatedArtifacts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "game.gb");
            await File.WriteAllTextAsync(output, "previous ROM");
            var result = await new RomCompiler(SyntheticRunner(), new FailingFileSystem { FailMoveNumber = 4 })
                .CompileAsync([Fixture], new RomBuildOptions(output));
            Assert.IsFalse(result.Success);
            Assert.IsEmpty(result.Artifacts);
            Assert.AreEqual("previous ROM", await File.ReadAllTextAsync(output));
            Assert.HasCount(1, Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [TestMethod]
    public async Task CleanupFailureRetainsOriginalDiagnosticOrPublishedSuccess()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "game.gb");
            var fs = new FailingFileSystem { FailCleanup = true };
            var success = await new RomCompiler(SyntheticRunner(), fs).CompileAsync([Fixture], new RomBuildOptions(output));
            Assert.IsTrue(success.Success, string.Join("\n", success.Diagnostics));
            Assert.HasCount(5, success.Artifacts);
            Assert.IsTrue(success.Artifacts.Values.All(File.Exists));
            StringAssert.Contains(success.Warnings.Single(), "GBC007: Could not remove build work directory");
            StringAssert.Contains(success.Warnings.Single(), "Remove it manually");

            var runner = new FakeRunner((tool, args, _) => args.SequenceEqual(["--version"])
                ? new(0, Path.GetFileNameWithoutExtension(tool) + " v0.9.0", "") : new(1, "", "original tool error"));
            var failure = await new RomCompiler(runner, fs).CompileAsync([Fixture], new RomBuildOptions(output));
            Assert.IsFalse(failure.Success);
            Assert.IsEmpty(failure.Artifacts);
            StringAssert.Contains(failure.Diagnostics[0], "original tool error");
            StringAssert.Contains(failure.Diagnostics[1], "GBC007:");
            Assert.IsNotNull(failure.WorkDirectory);
            Assert.IsTrue(Directory.Exists(failure.WorkDirectory));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static FakeRunner SyntheticRunner()
    {
        var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([(Fixture, File.ReadAllText(Fixture))])).Layout!;
        return new FakeRunner((tool, args, work) =>
        {
            var name = Path.GetFileNameWithoutExtension(tool);
            if (args.SequenceEqual(["--version"])) return new(0, name + " v0.9.0", "");
            if (name == "rgbasm") File.WriteAllBytes(Path.Combine(work, "game.o"), []);
            if (name == "rgblink")
            {
                var rom = new byte[0x8000];
                rom[0x100] = 0; rom[0x101] = 0xC3; rom[0x102] = 0x50; rom[0x103] = 1;
                Convert.FromHexString("CEED6666CC0D000B03730083000C000D0008111F8889000EDCCC6EE6DDDDD999BBBB67636E0EECCCDDDC999FBBB9333E")
                    .CopyTo(rom, 0x104);
                int checksum = 0;
                for (int i = 0x134; i <= 0x14C; i++) checksum = (checksum - rom[i] - 1) & 0xFF;
                rom[0x14D] = (byte)checksum;
                int global = 0;
                for (int i = 0; i < rom.Length; i++) if (i is not (0x14E or 0x14F)) global = (global + rom[i]) & 0xFFFF;
                rom[0x14E] = (byte)(global >> 8); rom[0x14F] = (byte)global;
                File.WriteAllBytes(Path.Combine(work, "game.gb"), rom);
                File.WriteAllText(Path.Combine(work, "game.map"), "new map");
                var symbols = new List<string> { "00:0150 gb_start", "00:0150 gb_entry",
                    $"00:{layout.StackTop:X4} gb_stack_top",
                    $"{(layout.StackBottom < 0xD000 ? 0 : 1):X2}:{layout.StackBottom:X4} gb_stack_bottom" };
                symbols.AddRange(layout.Allocations.Select(slot =>
                    $"{(slot.Address < 0xD000 ? 0 : 1):X2}:{slot.Address:X4} gb_ram_{slot.Address:X4}"));
                File.WriteAllLines(Path.Combine(work, "game.sym"), symbols);
            }
            return new(0, "", "");
        });
    }

    private sealed class FailingFileSystem : IRomArtifactFileSystem
    {
        private readonly SystemRomArtifactFileSystem inner = new();
        public int? FailMoveNumber { get; init; }
        public bool FailCleanup { get; init; }
        public int PublishedMoves { get; private set; }
        public bool Exists(string path) => inner.Exists(path);
        public void Copy(string source, string destination, bool overwrite = false) => inner.Copy(source, destination, overwrite);
        public void Move(string source, string destination, bool overwrite)
        {
            inner.Move(source, destination, overwrite);
            if (Path.GetFileName(source).StartsWith("stage", StringComparison.Ordinal) &&
                ++PublishedMoves == FailMoveNumber)
                throw new IOException("injected mid-publication fault after replacement");
        }
        public void Delete(string path) => inner.Delete(path);
        public void DeleteDirectory(string path)
        {
            if (FailCleanup) throw new IOException("injected cleanup fault");
            inner.DeleteDirectory(path);
        }
    }

    [TestMethod]
    public async Task RealRgbdsRomExecutesBoundedInSharpBoy()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN to the RGBDS 0.9.0 binary directory for ROM execution acceptance.");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var result = await new RomCompiler().CompileAsync([Fixture],
                new RomBuildOptions(Path.Combine(folder, "arithmetic.gb"), "ARITHMETIC", bin));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            Assert.HasCount(5, result.Artifacts);
            Assert.IsTrue(result.Artifacts.Values.All(File.Exists));
            Assert.IsEmpty(Directory.GetDirectories(folder));
            var rom = await File.ReadAllBytesAsync(result.Artifacts["gb"]);
            var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([(Fixture, await File.ReadAllTextAsync(Fixture))])).Layout!;
            var globals = layout.Allocations.Where(a => a.Name.StartsWith("global ", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(2, globals);
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = rom });
            foreach (var global in globals)
                for (var i = 0; i < global.Bytes; i++) gameboy.Memory.Write8(0xFF, (ushort)(global.Address + i));
            const int budget = 10000;
            int steps;
            for (steps = 0; steps < budget; steps++)
            {
                if (gameboy.Memory.Peek(0xC100) == 42 && gameboy.Memory.Peek(0xC101) == 0xA5) break;
                gameboy.RunStep();
            }
            Assert.IsLessThan(budget, steps, $"WRAM result/sentinel: {gameboy.Memory.Peek(0xC100):X2}/{gameboy.Memory.Peek(0xC101):X2}");
            Console.WriteLine($"RGBDS 0.9.0 / SharpBoy: result $C100=42, sentinel $C101=$A5 in {steps} instructions (budget {budget}).");
            Assert.AreEqual(42, gameboy.Memory.Peek(0xC100));
            Assert.AreEqual(0xA5, gameboy.Memory.Peek(0xC101));
            foreach (var global in globals)
                for (var i = 0; i < global.Bytes; i++)
                    Assert.AreEqual((byte)(global.InitialBits!.Value >> (i * 8)), gameboy.Memory.Peek((ushort)(global.Address + i)),
                        $"Global {global.Name} byte {i} must be explicitly initialized.");
            var sym = await File.ReadAllTextAsync(result.Artifacts["sym"]);
            Assert.IsTrue(sym.Contains("gb_stack_bottom", StringComparison.Ordinal));
            Assert.IsEmpty(RomValidator.Validate(rom, sym, layout));
            foreach (var (offset, code) in new[] { (0x104, "GBR002"), (0x100, "GBR003"), (0x147, "GBR004"),
                (0x14D, "GBR005"), (0x14E, "GBR006") })
            {
                var bad = (byte[])rom.Clone();
                bad[offset] ^= 1;
                Assert.IsTrue(RomValidator.Validate(bad, sym, layout).Any(d => d.Contains(code, StringComparison.Ordinal)), code);
            }
            Assert.IsTrue(RomValidator.Validate(rom, sym.Replace("gb_entry", "missing_entry", StringComparison.Ordinal), layout)
                .Any(d => d.StartsWith("GBR007:", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class FakeRunner(Func<string, IReadOnlyList<string>, string, ToolResult> callback) : IToolRunner
    {
        public List<(string Tool, string WorkingDirectory)> Invocations { get; } = [];
        public Task<ToolResult> RunAsync(string executable, string argument, CancellationToken cancellationToken = default) =>
            RunAsync(executable, [argument], Environment.CurrentDirectory, cancellationToken);
        public Task<ToolResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add((executable, workingDirectory));
            return Task.FromResult(callback(executable, arguments, workingDirectory));
        }
    }
}
