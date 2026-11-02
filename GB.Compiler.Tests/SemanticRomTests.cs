using System.Diagnostics;
using GB.Compiler;
using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class SemanticRomTests
{
    private static string Pure => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SemanticPure.cs");
    private static string Game => Path.Combine(AppContext.BaseDirectory, "Fixtures", "SemanticGame.cs");

    [TestMethod]
    public async Task RealScrollingRomRunsUnderIndependentPyBoy()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN to RGBDS 0.9.0.");
        var python = Environment.GetEnvironmentVariable("GB_PYBOY_PYTHON");
        if (string.IsNullOrWhiteSpace(python)) Assert.Inconclusive("Set GB_PYBOY_PYTHON to Python with PyBoy 2.6.0.");
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-pyboy-scroll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "scroll.gb");
            var sample = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScrollingBackground.cs");
            var result = await new RomCompiler().CompileAsync([sample], new RomBuildOptions(output, "SCROLLING", bin, Assets: SampleAssets.Background));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "IndependentEmulator", "validate.py"),
                "--rom", output, "--scroll", "--frames", "10" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch Python");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("PyBoy scrolling validation exceeded 60 seconds");
            }
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            Assert.AreEqual(0, process.ExitCode, $"PyBoy scrolling failed:\n{stdout}\n{stderr}");
            StringAssert.Contains(stdout, "PASS: PyBoy 2.6.0 scroll frame");
            Console.WriteLine(stdout.Trim());
        }
        finally { Directory.Delete(folder, true); }
    }

    private static byte[] HostBytes()
    {
        SemanticPure.Zero = 0;
        SemanticPure.Constant = 0x12345678;
        SemanticPure.Counter = 0;
        Assert.AreEqual((byte)1, SemanticPure.ByteWrap());
        Assert.AreEqual((sbyte)-1, SemanticPure.SignedWrap());
        Assert.AreEqual((ushort)1, SemanticPure.WordWrap());
        Assert.AreEqual(65536, SemanticPure.Carry16());
        Assert.AreEqual(-1, SemanticPure.Borrow16());
        Assert.AreEqual(int.MinValue, SemanticPure.Carry32());
        Assert.AreEqual(int.MaxValue, SemanticPure.Borrow32());
        Assert.AreEqual((byte)0x78, SemanticPure.Truncate32());
        Assert.AreEqual(-1, SemanticPure.SignExtend());
        Assert.AreEqual(255, SemanticPure.ZeroExtend());
        Assert.AreEqual((byte)0x21, SemanticPure.ShortCircuit(false));
        Assert.AreEqual((byte)1, SemanticPure.Counter);
        Assert.AreEqual((byte)0x31, SemanticPure.ShortCircuit(true));
        Assert.AreEqual((byte)1, SemanticPure.Counter);
        Assert.AreEqual((byte)13, SemanticPure.Loop());
        Assert.AreEqual(10, SemanticPure.NestedCalls());
        Assert.AreEqual((byte)255, SemanticPure.ArgumentOrder());
        Assert.AreEqual((byte)0x81, SemanticPure.LocalCompound());
        // These expressions mirror the fixture's WRAM writes, but all pure operations
        // are executed by the CLR from the very same SemanticPure.cs input file.
        return
        [
            SemanticPure.Zero, unchecked((byte)SemanticPure.Constant), SemanticPure.ByteWrap(),
            unchecked((byte)SemanticPure.SignedWrap()), unchecked((byte)SemanticPure.WordWrap()),
            unchecked((byte)SemanticPure.Carry16()), unchecked((byte)(SemanticPure.Carry16() - 65535)),
            unchecked((byte)SemanticPure.Borrow16()), unchecked((byte)SemanticPure.Carry32()),
            unchecked((byte)(SemanticPure.Carry32() - 2147483520)), unchecked((byte)SemanticPure.Borrow32()),
            SemanticPure.Truncate32(), unchecked((byte)SemanticPure.SignExtend()), unchecked((byte)SemanticPure.ZeroExtend()),
            SemanticPure.SignedLess(), SemanticPure.UnsignedGreater(), SemanticPure.ShortCircuit(false),
            SemanticPure.ShortCircuit(true), SemanticPure.Loop(),
            unchecked((byte)SemanticPure.NestedCalls()), SemanticPure.ArgumentOrder(), SemanticPure.LocalCompound(),
            SemanticPure.Counter, unchecked((byte)(SemanticPure.Constant - 0x12345622)),
            unchecked((byte)(SemanticPure.Constant - 0x12345644))
        ];
    }

    [TestMethod]
    public async Task RealRomMatchesHostUnderSharpBoyAndPyBoy()
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN to RGBDS 0.9.0.");
        var python = Environment.GetEnvironmentVariable("GB_PYBOY_PYTHON");
        if (string.IsNullOrWhiteSpace(python)) Assert.Inconclusive("Set GB_PYBOY_PYTHON to Python with PyBoy 2.6.0.");
        var expected = HostBytes();
        Assert.HasCount(25, expected);
        Assert.AreEqual((byte)0, expected[0]);
        Assert.AreEqual((byte)0x78, expected[1]);
        Assert.AreEqual((byte)0x80, expected[9]);
        Assert.AreEqual((byte)0x21, expected[16]);
        Assert.AreEqual((byte)0x31, expected[17]);
        Assert.AreEqual((byte)2, expected[22]);
        var folder = Path.Combine(Path.GetTempPath(), "sharpboy-semantic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "semantic.gb");
            var result = await new RomCompiler().CompileAsync([Pure, Game], new RomBuildOptions(output, "SEMANTIC", bin));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var rom = await File.ReadAllBytesAsync(output);
            var symbols = await File.ReadAllTextAsync(result.Artifacts["sym"]);
            var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower(
                [(Pure, await File.ReadAllTextAsync(Pure)), (Game, await File.ReadAllTextAsync(Game))])).Layout!;
            Assert.IsFalse(layout.Allocations.Any(slot => slot.Address < 0xC21A &&
                slot.Address + slot.Bytes > 0xC200), "Result buffer must not alias static spills");
            Assert.IsEmpty(RomValidator.Validate(rom, symbols, layout));
            Assert.HasCount(0x8000, rom);
            CollectionAssert.AreEqual(new byte[] { 0, 0xC3, 0x50, 1 }, rom.Skip(0x100).Take(4).ToArray());
            Assert.AreEqual((byte)0, rom[0x147]);
            Assert.AreEqual((byte)0, rom[0x148]);
            Assert.AreEqual((byte)0, rom[0x149]);
            int header = 0;
            for (var i = 0x134; i <= 0x14C; i++) header = (header - rom[i] - 1) & 0xff;
            Assert.AreEqual((byte)header, rom[0x14D]);
            int global = 0;
            for (var i = 0; i < rom.Length; i++) if (i is not (0x14E or 0x14F)) global = (global + rom[i]) & 0xffff;
            Assert.AreEqual((byte)(global >> 8), rom[0x14E]);
            Assert.AreEqual((byte)global, rom[0x14F]);

            var gb = new Gameboy();
            gb.Load(new Cartridge { Data = rom });
            var globals = layout.Allocations.Where(a => a.Name.StartsWith("global ", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(3, globals);
            foreach (var slot in globals)
                for (int i = 0; i < slot.Bytes; i++) gb.Memory.Write8(0xCC, (ushort)(slot.Address + i));
            for (int i = 0; i <= expected.Length; i++) gb.Memory.Write8(0xCC, (ushort)(0xC200 + i));
            const int budget = 100000;
            int steps;
            for (steps = 0; steps < budget && gb.Memory.Peek(0xC219) != 0xA5; steps++) gb.RunStep();
            Assert.IsLessThan(budget, steps, "Semantic ROM completion sentinel timed out");
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], gb.Memory.Peek((ushort)(0xC200 + i)), $"WRAM ${0xC200 + i:X4}");
            Assert.AreEqual((byte)0xA5, gb.Memory.Peek(0xC219));
            foreach (var slot in globals)
                for (int i = 0; i < slot.Bytes; i++)
                    Assert.AreEqual(slot.Name == "global Counter" ? expected[22] :
                        (byte)(slot.InitialBits!.Value >> (i * 8)),
                        gb.Memory.Peek((ushort)(slot.Address + i)), $"initialized {slot.Name} byte {i}");
            Console.WriteLine($"SharpBoy semantic: 26 WRAM bytes in {steps}/{budget} instructions");

            var start = new ProcessStartInfo(python)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "IndependentEmulator", "validate.py"),
                "--rom", output, "--address", "C200", "--expected", Convert.ToHexString(expected),
                "--sentinel", "A5", "--frames", "10" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch Python");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("PyBoy validation exceeded 60 seconds");
            }
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            Assert.AreEqual(0, process.ExitCode, $"PyBoy failed:\n{stdout}\n{stderr}");
            StringAssert.Contains(stdout, "PASS: PyBoy 2.6.0 frame");
            StringAssert.Contains(stdout, Convert.ToHexString(expected) + "A5");
            Console.WriteLine(stdout.Trim());
        }
        finally { Directory.Delete(folder, true); }
    }
}
