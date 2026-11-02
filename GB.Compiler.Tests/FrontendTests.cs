using GB.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class FrontendTests
{
    private const string Header = "using GB.Sdk; static class Game { [GameEntryPoint] static void Start() { ";
    private const string Footer = " } }";

    private static FrontendResult Analyze(string text) => GameFrontend.Analyze([("test.cs", text)]);

    private static void AssertLocated(FrontendResult result, string id, string path, int line, int column) =>
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id && d.Path == path && d.Line == line && d.Column == column),
            string.Join("\n", result.Diagnostics));

    [TestMethod]
    public void PositiveProgramAndReachableGraph()
    {
        var result = Analyze("""
            using GB.Sdk;
            static class Game {
                static byte Count = 3;
                const short Limit = -2;
                [GameEntryPoint] public static void Start() {
                    int n = Count + (ushort)65535;
                    if (n > 65535 && Count != 0) Count = unchecked((byte)n);
                    while (Count != 0) { Tick(Count); Count--; }
                }
                static void Tick(byte value) => GameMemory.WriteByte(0xC000, value);
                static void Orphan() { byte x = 3; x += 2; }
            }
            """);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("Start", result.EntryPoint?.Name);
        Assert.HasCount(2, result.ReachableCalls);
        Assert.AreEqual("Tick", result.ReachableCalls[result.EntryPoint!].Single().Name);
    }

    [TestMethod]
    public void RoslynErrorsPreemptSubsetValidation()
    {
        var result = Analyze(Header + "int x = ; for (;;) {}" + Footer);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id.StartsWith("CS", StringComparison.Ordinal)));
        Assert.IsFalse(result.Diagnostics.Any(d => d.Id.StartsWith("GBL", StringComparison.Ordinal)));
        Assert.IsNull(result.EntryPoint);
        Assert.IsEmpty(result.ReachableCalls);
    }

    [TestMethod]
    [DataRow("static class Game { }", "GBL001")]
    [DataRow("using GB.Sdk; static class Game { [GameEntryPoint] static void A() {} [GameEntryPoint] static void B() {} }", "GBL001")]
    [DataRow("using GB.Sdk; static class Game { [GameEntryPoint] static int Start() => 1; }", "GBL001")]
    [DataRow("using GB.Sdk; class Game { [GameEntryPoint] static void Start() {} }", "GBL001")]
    [DataRow(Header + "string x = \"hello\";" + Footer, "GBL002")]
    [DataRow(Header + "int[] xs = new int[2];" + Footer, "GBL002")]
    [DataRow(Header + "object o = 1;" + Footer, "GBL007")]
    [DataRow(Header + "int x = 2 * 3;" + Footer, "GBL006")]
    [DataRow(Header + "int x = 4 << 1;" + Footer, "GBL006")]
    [DataRow(Header + "int x = 4 / 2;" + Footer, "GBL006")]
    [DataRow(Header + "int x = 4 % 2;" + Footer, "GBL006")]
    [DataRow(Header + "int x = 1; x *= 2;" + Footer, "GBL006")]
    [DataRow(Header + "int x = true ? 1 : 2;" + Footer, "GBL006")]
    [DataRow(Header + "for (int i = 0; i < 2; i++) {}" + Footer, "GBL005")]
    [DataRow(Header + "switch (1) { case 1: break; }" + Footer, "GBL005")]
    [DataRow(Header + "int x = 1; do { x--; } while (x != 0);" + Footer, "GBL005")]
    [DataRow(Header + "System.Func<int> f = () => 1;" + Footer, "GBL002")]
    [DataRow(Header + "System.Console.WriteLine(1);" + Footer, "GBL008")]
    [DataRow(Header + "System.Math.Abs(-1);" + Footer, "GBL008")]
    [DataRow(Header + "Start();" + Footer, "GBL008")]
    [DataRow("using GB.Sdk; static class Game { static byte x = Read(); static byte Read() => 1; [GameEntryPoint] static void Start() {} }", "GBL004")]
    [DataRow("using GB.Sdk; static class Game { static Game() {} [GameEntryPoint] static void Start() {} }", "GBL003")]
    [DataRow("using GB.Sdk; static class Game { static int Value { get; set; } [GameEntryPoint] static void Start() {} }", "GBL003")]
    [DataRow("using GB.Sdk; static class Game { static readonly byte Value = 2; [GameEntryPoint] static void Start() {} }", "GBL003")]
    [DataRow("using GB.Sdk; static class Game { static void Bad<T>() {} [GameEntryPoint] static void Start() {} }", "GBL003")]
    [DataRow("using GB.Sdk; static class Game { static void Bad() { void Local() {} } [GameEntryPoint] static void Start() {} }", "GBL003")]
    [DataRow("using GB.Sdk; enum GameType { One } static class Game { [GameEntryPoint] static void Start() {} }", "GBL003")]
    [DataRow(Header + "int a = 1; int x = checked(a + 1);" + Footer, "GBL007")]
    [DataRow(Header + "int x = 1; object o = x;" + Footer, "GBL007")]
    public void RejectedSubsetHasLocatedDiagnostic(string source, string id)
    {
        var result = Analyze(source);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id == id && d.Path == "test.cs" && d.Line > 0 && d.Column > 0),
            string.Join("\n", result.Diagnostics));
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void AllSubmittedSourcesAreValidatedEvenWhenUnreachable()
    {
        var result = GameFrontend.Analyze([
            ("entry.cs", Header + Footer),
            ("other.cs", "static class Unused { static void Bad() { for (;;) break; } }")]);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "GBL005" && d.Path == "other.cs"));
    }

    [TestMethod]
    [DataRow("long unused;")]
    [DataRow("object unused;")]
    public void UnsupportedUninitializedLocalInEntryIsRejected(string declaration)
    {
        var source = Header + declaration + Footer;
        var result = Analyze(source);
        AssertLocated(result, "GBL002", "test.cs", 1, source.IndexOf("unused", StringComparison.Ordinal) + 1);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    [DataRow("long unused;")]
    [DataRow("object unused;")]
    public void UnsupportedUninitializedLocalInUnreachableMethodIsRejected(string declaration)
    {
        var other = "static class Unused { static void Helper() { " + declaration + " } }";
        var result = GameFrontend.Analyze([("entry.cs", Header + Footer), ("other.cs", other)]);
        AssertLocated(result, "GBL002", "other.cs", 1, other.IndexOf("unused", StringComparison.Ordinal) + 1);
        Assert.HasCount(1, result.ReachableCalls);
    }

    [TestMethod]
    public void SupportedUninitializedLocalIsAccepted()
    {
        var result = Analyze(Header + "int unused;" + Footer);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    [DataRow("static extern void Helper();")]
    [DataRow("static partial void Helper();")]
    public void UnreachableDeclarationOnlyMethodsAreRejected(string declaration)
    {
        var source = Header + Footer.Replace(" } }", " } " + declaration + " }");
        if (declaration.Contains("partial", StringComparison.Ordinal))
            source = source.Replace("static class Game", "static partial class Game", StringComparison.Ordinal);
        var result = Analyze(source);
        AssertLocated(result, "GBL003", "test.cs", 1, source.IndexOf("Helper", StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    public void ReachableExternHelperIsRejected()
    {
        var source = Header + "Helper();" + Footer.Replace(" } }", " } static extern void Helper(); }");
        var result = Analyze(source);
        AssertLocated(result, "GBL003", "test.cs", 1, source.IndexOf("Helper();", source.IndexOf("static extern", StringComparison.Ordinal), StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    public void UnreachableAbstractDeclarationOnlyMethodIsRejected()
    {
        const string other = "abstract class Unused { public abstract void Helper(); }";
        var result = GameFrontend.Analyze([("entry.cs", Header + Footer), ("other.cs", other)]);
        AssertLocated(result, "GBL003", "other.cs", 1, other.IndexOf("Helper", StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    public void ConstantFieldInitializerMustUseAllowedOperators()
    {
        const string source = "using GB.Sdk; static class Game { static int Value = 2 * 3; [GameEntryPoint] static void Start() {} }";
        var result = Analyze(source);
        AssertLocated(result, "GBL006", "test.cs", 1, source.IndexOf("2 * 3", StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    public void AllowedConstantFieldInitializerIsAccepted()
    {
        var result = Analyze("using GB.Sdk; static class Game { static int Value = 2 + 3; [GameEntryPoint] static void Start() {} }");
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    public void ExternalCallInUnreachableSubmittedMethodIsRejected()
    {
        const string other = "static class Unused { static void Bad() { System.Console.WriteLine(1); } }";
        var result = GameFrontend.Analyze([("entry.cs", Header + Footer), ("other.cs", other)]);
        AssertLocated(result, "GBL008", "other.cs", 1, other.IndexOf("System.Console.WriteLine", StringComparison.Ordinal) + 1);
        Assert.HasCount(1, result.ReachableCalls);
    }

    [TestMethod]
    public void ImplicitBoxingIsRejectedAtConvertedExpression()
    {
        var source = Header + "int x = 1; object boxed = x;" + Footer;
        var result = Analyze(source);
        AssertLocated(result, "GBL007", "test.cs", 1, source.IndexOf("x;", StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    public void MultipleFilesContributeToEntryPointCount()
    {
        const string second = "using GB.Sdk; static class Other { [GameEntryPoint] static void Second() {} }";
        var result = GameFrontend.Analyze([
            ("first.cs", Header + Footer), ("second.cs", second)]);
        AssertLocated(result, "GBL001", "second.cs", 1, second.IndexOf("Second", StringComparison.Ordinal) + 1);
        Assert.IsNull(result.EntryPoint);
    }

    [TestMethod]
    public void ExternalWriteByteLookalikeWithoutSdkMarkerIsRejected()
    {
        const string call = "System.Runtime.InteropServices.Marshal.WriteByte(System.IntPtr.Zero, (byte)1);";
        var source = Header + call + Footer;
        var result = Analyze(source);
        AssertLocated(result, "GBL008", "test.cs", 1, source.IndexOf(call, StringComparison.Ordinal) + 1);
    }

    [TestMethod]
    public void RecursionDiagnosticPointsToBackEdgeInvocation()
    {
        var source = string.Join("\n", [
            "using GB.Sdk;",
            "static class Game {",
            "    [GameEntryPoint] static void Start() { A(); }",
            "    static void A() { B(); }",
            "    static void B() { A(); }",
            "}"]);
        var result = Analyze(source);
        AssertLocated(result, "GBL008", "test.cs", 5, "    static void B() { A(); }".IndexOf("A();", StringComparison.Ordinal) + 1);
        Assert.HasCount(3, result.ReachableCalls);
    }

    [TestMethod]
    public void MutualRecursionIsReachabilitySensitive()
    {
        const string cycle = "static void A() => B(); static void B() => A();";
        var reachable = Analyze(Header + "A();" + Footer.Replace(" } }", " } " + cycle + " }") );
        Assert.IsTrue(reachable.Diagnostics.Any(d => d.Id == "GBL008"), string.Join("\n", reachable.Diagnostics));

        var unreachable = Analyze(Header + Footer.Replace(" } }", " } " + cycle + " }") );
        Assert.IsTrue(unreachable.Success, string.Join("\n", unreachable.Diagnostics));
        Assert.HasCount(1, unreachable.ReachableCalls);
    }

    [TestMethod]
    public void NameLookalikeIsNotSdkIntrinsic()
    {
        var result = Analyze("""
            using GB.Sdk;
            static class GameMemory { public static void WriteByte(ushort a, byte b) {} }
            static class Game { [GameEntryPoint] static void Start() { GameMemory.WriteByte(0xC000, 1); } }
            """);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("WriteByte", result.ReachableCalls[result.EntryPoint!].Single().Name);
    }

    [TestMethod]
    public void SdkNamespaceSpoofDoesNotOverrideSdkIdentity()
    {
        var result = Analyze("""
            namespace GB.Sdk { public static class GameMemory { public static void WriteByte(ushort a, byte b) {} } }
            namespace Program { static class Game { [GB.Sdk.GameEntryPoint] static void Start() { GB.Sdk.GameMemory.WriteByte(0xC000, 1); } } }
            """);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.AreEqual("GB.Sdk", result.ReachableCalls[result.EntryPoint!].Single().ContainingNamespace.ToDisplayString());
        Assert.HasCount(2, result.ReachableCalls);
    }

    [TestMethod]
    public void FakeEntryAttributeCannotImpersonateSdk()
    {
        var result = Analyze("""
            namespace GB.Sdk { public sealed class GameEntryPointAttribute : System.Attribute {} }
            static class Game { [GB.Sdk.GameEntryPoint] static void Start() {} }
            """);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Id == "GBL001"), string.Join("\n", result.Diagnostics));
    }

    [TestMethod]
    public void PositiveSideEffectsAndConversions()
    {
        var result = Analyze("""
            using GB.Sdk;
            static class Game {
                const byte Seed = 2;
                static short Last = -1;
                [GameEntryPoint] static void Start() {
                    byte x = Seed;
                    int y = (ushort)65535 + 1;
                    bool ok = x++ > 0 & Last < 0;
                    if (ok || Touch(x) && false) Last = (short)y;
                    x += 1; x &= 15; x ^= 3; x |= 2;
                    while (x != 0) { --x; if (x == 2) continue; if (x == 1) break; }
                }
                static bool Touch(byte value) { Last -= value; return true; }
            }
            """);
        Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
        Assert.HasCount(2, result.ReachableCalls);
    }
}
