using GB.Compiler;
using GB.Emulator.Core;
using GB.Sdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class TextAndMemoryTests
{
    private static string Fixture(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
    private static string Program(string body) => "using GB.Sdk; static class G { [GameEntryPoint] static void Start() { " + body + " } }";

    [TestMethod]
    public void ReadAndTextHaveOrderedIrAndNoHostImplementation()
    {
        var module = GameIrCompiler.AnalyzeAndLower([("text.cs", Program("byte x = GameMemory.ReadByte(0xA002); GameMemory.WriteByte(0xC001, x); GameText.Write(0x9800, \"SDK\");"))]);
        var instructions = module.Functions.Single().Blocks.SelectMany(b => b.Instructions).ToArray();
        Assert.AreEqual(IrEffect.HardwareReadWrite, instructions.OfType<IrReadByte>().Single().Effect);
        Assert.IsLessThan(Array.FindIndex(instructions, i => i is IrWriteByte), Array.FindIndex(instructions, i => i is IrReadByte));
        Assert.AreEqual("SDK", instructions.OfType<IrText>().Single().Text);
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameMemory.ReadByte(0xA002));
        Assert.ThrowsExactly<PlatformNotSupportedException>(() => GameText.Write(0x9800, "SDK"));
        var lookalike = GameIrCompiler.AnalyzeAndLower([("lookalike.cs", "using GB.Sdk; namespace GB.Sdk { static class GameMemory { public static byte ReadByte(ushort address) { return 7; } } } " + Program("byte x = GameMemory.ReadByte(0xA002);").Replace("using GB.Sdk;", ""))]);
        Assert.IsFalse(lookalike.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrReadByte));
    }

    [TestMethod]
    [DataRow("GameText.Write(0x9800, null);")]
    [DataRow("GameText.Write(0x9800, \"\\n\");")]
    [DataRow("GameText.Write(0x9800, \"é\");")]
    [DataRow("GameText.Write(0x9800, \"123456789012345678901234567890123\");")]
    [DataRow("string value = \"SDK\"; GameText.Write(0x9800, value);")]
    public void TextIsCompileTimeOnly(string body)
    {
        var result = GameFrontend.Analyze([("invalid.cs", Program(body))]);
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id is "GBL011" or "GBL002"), string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    public void FontSizedBackgroundsValidateBoundsAndSpriteOverlap()
    {
        var layout = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([("font.cs", Program("GameDisplay.InitializeBackground();"))])).Layout!;
        var font = GameFont.CreateTiles();
        Assert.HasCount(2048, font);
        Assert.IsEmpty(new GameAssetBundle(font, Enumerable.Repeat((byte)32, 1024).ToArray()).Validate(layout));
        Assert.IsEmpty(new GameAssetBundle(new byte[4096], Enumerable.Repeat((byte)255, 1024).ToArray()).Validate(layout));
        Assert.IsNotEmpty(new GameAssetBundle(new byte[4097], new byte[1024]).Validate(layout));
        Assert.IsNotEmpty(new GameAssetBundle(new byte[16], Enumerable.Repeat((byte)1, 1024).ToArray()).Validate(layout));
        var sprites = Sm83Backend.Emit(GameIrCompiler.AnalyzeAndLower([("sprite.cs", Program("GameDisplay.InitializeBackground(); GameSprites.Initialize();"))])).Layout!;
        StringAssert.Contains(string.Join("\n", new GameAssetBundle(font, new byte[1024], new byte[16]).Validate(sprites)), "overlap");
    }

    [TestMethod]
    public async Task NativeTextMemoryAndNarrowArithmeticPreserveValuesAndMapBounds()
    {
        string body = """
            GameDisplay.InitializeBackground(); GameDisplay.WaitForNextVBlank(); GameMemory.WriteByte(0xFF40, 0);
            GameMemory.WriteByte(0xC000, 7);
            GameText.Write(0xC000, "CORRUPT"); GameText.Write(0x9BFF, "AB");
            GameText.Write(0x9800, "SDK");
            GameTextNumbers.WriteDecimal(0x9820, 0); GameTextNumbers.WriteDecimal(0x9840, 9);
            GameTextNumbers.WriteDecimal(0x9860, 10); GameTextNumbers.WriteDecimal(0x9880, 99);
            GameTextNumbers.WriteDecimal(0x98A0, 100); GameTextNumbers.WriteDecimal(0x98C0, 101);
            GameTextNumbers.WriteDecimal(0x98E0, 255);
            byte b = 255; ushort u = 65535; sbyte s = -1; int i = 2147483647;
            if (b > 127 && b > -1 && u > 32767 && s < b && i + 1 < 0)
                GameMemory.WriteByte(0xC001, (byte)(b + 2));
            GameMemory.WriteByte(0xC002, GameMemory.ReadByte(0xC000));
            GameMemory.WriteByte(0xC003, 0xA5);
            """;
        var directory = Path.Combine(Path.GetTempPath(), "sharpboy-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "text.cs");
            await File.WriteAllTextAsync(source, Program(body));
            var path = Path.Combine(directory, "text.gb");
            var result = await new RomCompiler().CompileAsync([source, Fixture("GameTextNumbers.cs")], Options(path));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var gb = new Gameboy(); gb.Load(new Cartridge { Data = await File.ReadAllBytesAsync(path) });
            for (int step = 0; step < 1_000_000 && gb.Memory.Peek(0xC003) != 0xA5; step++) gb.RunStep();
            Assert.AreEqual((byte)0xA5, gb.Memory.Peek(0xC003));
            Assert.AreEqual((byte)1, gb.Memory.Peek(0xC001));
            Assert.AreEqual((byte)7, gb.Memory.Peek(0xC002));
            Assert.AreEqual((byte)32, gb.Memory.Peek(0x9BFF));
            Assert.AreEqual((byte)'S', gb.Memory.Peek(0x9800));
            var expected = new[] { "0", "9", "10", "99", "100", "101", "255" };
            for (int row = 0; row < expected.Length; row++)
                for (int c = 0; c < expected[row].Length; c++)
                    Assert.AreEqual((byte)expected[row][c], gb.Memory.Peek((ushort)(0x9820 + row * 32 + c)));
            var font = GameFont.CreateTiles();
            for (int offset = 0; offset < font.Length; offset++)
                Assert.AreEqual(font[offset], gb.Memory.Peek((ushort)(0x8000 + offset)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public async Task CheckedInDhcpFirmwareIsExactlyTheSdkCompilerOutput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sharpboy-dhcp-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "dhcp.gb");
            var result = await new RomCompiler().CompileAsync([Fixture("DhcpEntryPoint.cs"), Fixture("NetworkDhcp.cs"), Fixture("GameTextNumbers.cs")], Options(path, "DHCP NETCART"));
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            CollectionAssert.AreEqual(await File.ReadAllBytesAsync(Fixture("NETWORK-DHCP.gb")), await File.ReadAllBytesAsync(path),
                "Rebuild the embedded SDK firmware using Samples/NetworkDhcp/Build.ps1.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("Telemetry", "GB TELEMETRY")]
    [DataRow("Worker", "GB NET WORKER")]
    public async Task NetworkFirmwareIsExactlyTheSdkCompilerOutput(string firmware, string title)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpboy-network-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "network.gb");
            string[] sources = firmware == "Telemetry"
                ? [Fixture("NetworkTelemetry.cs"), Fixture("NetworkDhcp.cs"), Fixture("GameTextNumbers.cs"), Fixture("GameNetwork.cs")]
                : [Fixture("NetworkWorker.cs"), Fixture("GameNetwork.cs")];
            var options = Options(path, title);
            if (firmware == "Worker") options = options with { Assets = null };
            var result = await new RomCompiler().CompileAsync(sources, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            CollectionAssert.AreEqual(await File.ReadAllBytesAsync(Fixture("NETWORK-" + firmware.ToUpperInvariant() + ".gb")),
                await File.ReadAllBytesAsync(path), "Rebuild using Samples/NetworkTelemetry/Build.ps1.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("Fractal", "GB FRACTAL")]
    [DataRow("Life", "GB LIFE")]
    public async Task VisualNetworkFirmwareRebuildsFromConstrainedSourceAndBlankLcdAssets(string firmware, string title)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpboy-fractal-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] map = Enumerable.Repeat((byte)192, 1024).ToArray();
            for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++) map[(y + 3) * 32 + x + 2] = (byte)(y * 16 + x);
            var options = Options(Path.Combine(directory, "network.gb"), title) with
            {
                Assets = new(new byte[193 * 16], map),
                Limits = new(WramStart: firmware == "Life" ? 0xC900 : 0xC800, WramBytes: firmware == "Life" ? 0x1700 : 0x1800)
            };
            string[] sources = firmware == "Life" ? [Fixture("NetworkLife.cs"), Fixture("GameNetwork.cs"), Fixture("GameNetworkStream.cs")]
                : [Fixture("NetworkFractal.cs"), Fixture("GameNetwork.cs")];
            var result = await new RomCompiler().CompileAsync(sources, options);
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            CollectionAssert.AreEqual(await File.ReadAllBytesAsync(Fixture("NETWORK-" + firmware.ToUpperInvariant() + ".gb")), await File.ReadAllBytesAsync(options.OutputPath));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static RomBuildOptions Options(string path, string title = "SDK TEXT")
    {
        var bin = Environment.GetEnvironmentVariable("GB_RGBDS_BIN");
        if (string.IsNullOrWhiteSpace(bin)) Assert.Inconclusive("Set GB_RGBDS_BIN to RGBDS 0.9.0.");
        return new(path, title, bin, Assets: new(GameFont.CreateTiles(), Enumerable.Repeat((byte)32, 1024).ToArray()),
            Limits: new(WramStart: 0xC100, WramBytes: 0x1F00));
    }
}
