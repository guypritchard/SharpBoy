using GB.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class Sm83BackendTests
{
    private static IrModule Lower(string body, string extra = "") => GameIrCompiler.AnalyzeAndLower([
        ("backend.cs", $"using GB.Sdk; static class Game {{ [GameEntryPoint] static void Start() {{ {body} }} {extra} }}")]);

    private static BackendResult Emit(string body, string extra = "", Sm83Limits? limits = null)
    {
        var result = Sm83Backend.Emit(Lower(body, extra), limits);
        Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
        Assert.IsNotNull(result.Assembly);
        Assert.IsNotNull(result.Layout);
        return result;
    }

    private static void InOrder(string text, params string[] parts)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var offset = 0;
        foreach (var part in parts)
        {
            var at = text.IndexOf(part, offset, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, at, $"Missing after offset {offset}: {part}\n{text}");
            offset = at + part.Length;
        }
    }

    private static void Rejected(IrModule module, string expected)
    {
        var error = Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
        StringAssert.Contains(error.Message, expected);
        var result = Sm83Backend.Emit(module);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Layout);
        Assert.IsNull(result.Assembly);
        Assert.IsNull(result.MemoryReport);
        Assert.AreEqual("GBB001", result.Diagnostics.Single().Id);
        StringAssert.Contains(result.Diagnostics.Single().Contributor, expected);
    }

    [TestMethod]
    public void ClonedValueOperandIsNotCanonical()
    {
        var loc = new IrLocation("identity.cs", 1, 1, 0, 1);
        var module = new IrModule("entry");
        var function = new IrFunction("entry", IrType.Void, loc);
        var block = new IrBlock("body", loc);
        var value = new IrValue("v", IrType.U8);
        module.Functions.Add(function);
        function.Blocks.Add(block);
        block.Instructions.Add(new IrLiteral(value, 1, loc));
        block.Instructions.Add(new IrUnary(new IrValue("result", IrType.U8), value with { }, IrUnaryKind.Plus, loc));
        block.Terminate(new IrReturn(null, loc));
        Rejected(module, "noncanonical value");
    }

    [TestMethod]
    public void ClonedParameterLocalAndGlobalAreNotCanonicalStorage()
    {
        var loc = new IrLocation("storage.cs", 2, 3, 10, 1);
        foreach (var (kind, cloneSlot) in new[]
            { (IrSlotKind.Parameter, true), (IrSlotKind.Local, true), (IrSlotKind.Local, false) })
        {
            var module = new IrModule("entry");
            var function = new IrFunction("entry", IrType.Void, loc);
            var block = new IrBlock("body", loc);
            var slot = new IrSlot("slot", IrType.U8, kind, loc);
            var global = new IrGlobal("global", IrType.U8, 0, loc);
            module.Functions.Add(function);
            module.Globals.Add(global);
            if (kind == IrSlotKind.Parameter) function.Parameters.Add(slot);
            else function.Locals.Add(slot);
            function.Blocks.Add(block);
            block.Instructions.Add(new IrLoad(new IrValue("v", IrType.U8),
                cloneSlot ? slot with { } : null, cloneSlot ? null : global with { }, loc));
            block.Terminate(new IrReturn(null, loc));
            Rejected(module, "invalid storage");
        }
    }

    [TestMethod]
    public void ClonedCallTargetIsNotCanonicalFunction()
    {
        var loc = new IrLocation("call.cs", 3, 4, 20, 1);
        var module = new IrModule("entry");
        var function = new IrFunction("entry", IrType.Void, loc);
        var target = new IrFunction("helper", IrType.Void, loc);
        module.Functions.AddRange([function, target]);
        var body = new IrBlock("body", loc);
        function.Blocks.Add(body);
        body.Instructions.Add(new IrCall(null, new IrFunction("helper", IrType.Void, loc), [], loc));
        body.Terminate(new IrReturn(null, loc));
        var helperBody = new IrBlock("body", loc);
        target.Blocks.Add(helperBody);
        helperBody.Terminate(new IrReturn(null, loc));
        Rejected(module, "invalid call target");
    }

    [TestMethod]
    public void RecursionDiagnosticPointsToBackEdgeCall()
    {
        var declaration = new IrLocation("recursion.cs", 1, 1, 0, 6);
        var callSite = new IrLocation("recursion.cs", 9, 8, 81, 5);
        var module = new IrModule("entry");
        var function = new IrFunction("entry", IrType.Void, declaration);
        var block = new IrBlock("body", declaration);
        module.Functions.Add(function);
        function.Blocks.Add(block);
        block.Instructions.Add(new IrCall(null, function, [], callSite));
        block.Terminate(new IrReturn(null, declaration));
        var error = Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
        Assert.AreEqual(callSite, error.Location);
        var result = Sm83Backend.Emit(module);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("GBB001", result.Diagnostics.Single().Id);
        Assert.AreEqual(callSite, result.Diagnostics.Single().Source);
        Assert.IsNull(result.Assembly);
        Assert.IsNull(result.Layout);
        Assert.IsNull(result.MemoryReport);
    }

    [TestMethod]
    public void NestedCallsHaveDistinctSpillsAndStackBound()
    {
        var result = Emit("int x = Add(1, Twice(2)); GameMemory.WriteByte(0xC100, (byte)x);",
            "static int Twice(int x) => x + x; static int Add(int a, int b) => a + b;");
        var layout = result.Layout!;
        Assert.AreEqual(4, layout.StackRequired); // startup->Start->Add/Twice (non-overlapping sibling calls)
        Assert.AreEqual(124, layout.StackHeadroom);
        Assert.AreEqual(0xE000, layout.StackTop);
        Assert.AreEqual(0xDF80, layout.StackBottom);
        var slots = layout.Allocations;
        Assert.IsGreaterThan(10, slots.Count);
        Assert.AreEqual(slots.Count, slots.Select(s => s.Address).Distinct().Count());
        Assert.IsTrue(slots.Zip(slots.Skip(1)).All(p => p.First.Address + p.First.Bytes <= p.Second.Address));
        StringAssert.Contains(result.Assembly!, "gb_entry::");
        StringAssert.Contains(result.Assembly!, "gb_stack_bottom::");
        StringAssert.Contains(result.Assembly!, "call gb_fn_");
        StringAssert.Contains(result.Assembly!, "ld [hl], a");
        StringAssert.Contains(result.MemoryReport!, "CPU stack $DF80-$DFFF");
        Assert.AreEqual(result.MemoryReport, layout.Report());
        Assert.IsFalse(result.Assembly!.Contains("push ", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DepthIsLongestPathRatherThanNumberOfCalls()
    {
        var result = Emit("A(); A();", "static void A() { B(); } static void B() { C(); } static void C() {} ");
        Assert.AreEqual(8, result.Layout!.StackRequired);
        Assert.IsEmpty(result.Layout.Allocations);
        Assert.AreEqual(result.Assembly, Sm83Backend.Emit(Lower("A(); A();",
            "static void A() { B(); } static void B() { C(); } static void C() {} ")).Assembly);
    }

    [TestMethod]
    public void GlobalInitializerIsStorageOnlyAndConstIsInline()
    {
        var module = Lower("int x = n + K; n = x;", "static int n = -1234; const int K = 4;");
        var result = Sm83Backend.Emit(module);
        Assert.IsTrue(result.Success);
        var global = result.Layout!.Allocations.Single(a => a.Name.StartsWith("global ", StringComparison.Ordinal));
        Assert.AreEqual(4, global.Bytes);
        Assert.AreEqual(0xC000, global.Address);
        Assert.AreEqual(-1234, module.Globals.Single().InitialValue);
        Assert.AreEqual(-1234, global.InitialBits);
        StringAssert.Contains(result.MemoryReport!, "initial bits $FFFFFB2E");
        Assert.IsFalse(result.Layout.Allocations.Any(a => a.Name.Contains(" K", StringComparison.Ordinal)));
        StringAssert.Contains(result.Assembly!, "gb_ram_C000::");
        StringAssert.Contains(result.Assembly!, "ROM0");
        Assert.IsFalse(result.Assembly!.Contains("$FB2E", StringComparison.Ordinal)); // startup must initialize n
    }

    [TestMethod]
    public void I32ArithmeticAndNegationPropagateCarryAndBorrow()
    {
        var assembly = Emit("int x = A() + B(); int y = A() - B(); int z = -x; GameMemory.WriteByte(0xC100, (byte)(x ^ y ^ z));",
            "static int A() => 65535; static int B() => 1;").Assembly!;
        Assert.AreEqual(3, assembly.Split("adc a, b", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(assembly, "sub b");
        StringAssert.Contains(assembly, "sbc a, b");
        InOrder(assembly, "ld b, a", "ld a, $00", "sub b", "ld b, a", "ld a, $00", "sbc a, b");
    }

    [TestMethod]
    public void ConversionsCopyLowBytesAndSignExtendFromHighSignBit()
    {
        var assembly = Emit("sbyte s = -1; byte b = (byte)s; short t = s; int x = t; ushort u = (ushort)x; int z = b; GameMemory.WriteByte(0xC101, (byte)(u + z));").Assembly!;
        InOrder(assembly, "rlca", "sbc a, a");
        Assert.IsGreaterThanOrEqualTo(3, assembly.Split("rlca", StringSplitOptions.None).Length);
        StringAssert.Contains(assembly, "xor a");
        StringAssert.Contains(assembly, "adc a, b");
        StringAssert.Contains(assembly, "ld [hl], a");
    }

    [TestMethod]
    public void SignedCompareFlipsTopSignAndUnsignedDoesNot()
    {
        var signed = Emit("short x = -1; short y = 1; bool b = x < y; if (b) GameMemory.WriteByte(0xC100, 1);").Assembly!;
        // C# promotes ushort relational operands to signed int, so exercise the IR's native
        // unsigned comparison with a separately verified, hand-built U16 IR fixture.
        var loc = new IrLocation("compare.cs", 1, 1, 0, 1);
        var module = new IrModule("start");
        var fn = new IrFunction("start", IrType.Void, loc);
        var block = new IrBlock("entry", loc);
        var left = new IrValue("left", IrType.U16);
        var right = new IrValue("right", IrType.U16);
        var result = new IrValue("result", IrType.Bool);
        module.Functions.Add(fn);
        fn.Blocks.Add(block);
        block.Instructions.Add(new IrLiteral(left, 65535, loc));
        block.Instructions.Add(new IrLiteral(right, 1, loc));
        block.Instructions.Add(new IrCompare(result, left, right, IrCompareKind.Less, false, loc));
        block.Terminate(new IrReturn(null, loc));
        var emitted = Sm83Backend.Emit(module);
        Assert.IsTrue(emitted.Success);
        var unsigned = emitted.Assembly!;
        Assert.AreEqual(2, signed.Split("xor $80", StringSplitOptions.None).Length - 1);
        Assert.IsFalse(unsigned.Contains("xor $80", StringComparison.Ordinal));
        foreach (var assembly in new[] { signed, unsigned })
        {
            InOrder(assembly, "cp b", "jp nz, .gen_", "jp .gen_", ".gen_");
            StringAssert.Contains(assembly, "jp c, .gen_");
        }
    }

    [TestMethod]
    public void BranchesLoopsAndWritesAreOrdered()
    {
        var result = Emit("byte i = 0; while (i < 3) { if (i != 1) GameMemory.WriteByte(0xC100, i); i++; } GameMemory.WriteByte(0xC101, i);");
        var assembly = result.Assembly!;
        StringAssert.Contains(assembly, "jp nz, gb_fn_");
        Assert.AreEqual(2, assembly.Split("ld [hl], a", StringSplitOptions.None).Length - 1);
        Assert.IsGreaterThan(2, assembly.Split("jp gb_fn_", StringSplitOptions.None).Length);
        Assert.AreEqual(assembly, Sm83Backend.Emit(Lower("byte i = 0; while (i < 3) { if (i != 1) GameMemory.WriteByte(0xC100, i); i++; } GameMemory.WriteByte(0xC101, i);")).Assembly);
    }

    [TestMethod]
    public void BitwiseAndBoolUnaryEmitDistinctStoredValues()
    {
        var assembly = Emit("int a = ~1; int b = +a; int c = (a & b) | (a ^ b); bool flag = true; flag = !flag; flag = flag ^ true; if (flag) GameMemory.WriteByte(0xC100, (byte)c);").Assembly!;
        StringAssert.Contains(assembly, "cpl");
        StringAssert.Contains(assembly, "and b");
        StringAssert.Contains(assembly, "or b");
        StringAssert.Contains(assembly, "xor b");
        InOrder(assembly, "xor $01", "ld [$", "xor b");
        StringAssert.Contains(assembly, "jp nz, gb_fn_");
    }

    [TestMethod]
    public void ComparisonTruthTablesAndLabelsCoverAllKindsAndWidths()
    {
        var location = new IrLocation("comparison.cs", 1, 1, 0, 1);
        foreach (var type in new[] { IrType.U8, IrType.I8, IrType.U16, IrType.I16, IrType.I32 })
        foreach (var kind in Enum.GetValues<IrCompareKind>())
        {
            var module = new IrModule("entry");
            var fn = new IrFunction("entry", IrType.Void, location);
            var block = new IrBlock("body", location);
            var lhs = new IrValue("lhs", type);
            var rhs = new IrValue("rhs", type);
            var output = new IrValue("output", IrType.Bool);
            module.Functions.Add(fn);
            fn.Blocks.Add(block);
            block.Instructions.Add(new IrLiteral(lhs, 1, location));
            block.Instructions.Add(new IrLiteral(rhs, 2, location));
            block.Instructions.Add(new IrCompare(output, lhs, rhs, kind, IrVerifier.Signed(type), location));
            block.Terminate(new IrReturn(null, location));
            var result = Sm83Backend.Emit(module);
            Assert.IsTrue(result.Success, string.Join("; ", result.Diagnostics));
            var assembly = result.Assembly!;
            var eqTrue = kind is IrCompareKind.Equal or IrCompareKind.LessEqual or IrCompareKind.GreaterEqual;
            var lessTrue = kind is IrCompareKind.NotEqual or IrCompareKind.Less or IrCompareKind.LessEqual;
            var greaterTrue = kind is IrCompareKind.NotEqual or IrCompareKind.Greater or IrCompareKind.GreaterEqual;
            foreach (var (name, truth) in new[] { ("eq", eqTrue), ("less", lessTrue), ("greater", greaterTrue) })
                InOrder(assembly, $"_{name}:\n    ld a, ${(truth ? 1 : 0):X2}");
            Assert.AreEqual(IrVerifier.Width(type) / 8, assembly.Split("cp b", StringSplitOptions.None).Length - 1);
            Assert.AreEqual(IrVerifier.Signed(type) ? 2 : 0,
                assembly.Split("xor $80", StringSplitOptions.None).Length - 1);
            ValidateInstructionSetAndLabels(result);
        }
    }

    [TestMethod]
    public void WRAMBankBoundaryAndStackSplitAreReservedWithoutCrossing()
    {
        var result = Emit("int a = 1; GameMemory.WriteByte(0xC100, (byte)a);", limits:
            new Sm83Limits(WramStart: 0xCFFE, WramBytes: 32, StackBytes: 4));
        Assert.AreEqual(0xD000, result.Layout!.Allocations[0].Address); // four-byte slot cannot straddle bank
        Assert.AreEqual(0xD01A, result.Layout.StackBottom);
        StringAssert.Contains(result.Assembly!, "WRAMX[$D000], BANK[1]");
        StringAssert.Contains(result.Assembly!, "SECTION \"GB CPU stack D01A\"");
        var split = Emit("", limits: new Sm83Limits(WramStart: 0xCFFC, WramBytes: 8, StackBytes: 8));
        StringAssert.Contains(split.Assembly!, "SECTION \"GB CPU stack CFFC\", WRAM0[$CFFC]");
        StringAssert.Contains(split.Assembly!, "SECTION \"GB CPU stack D000\", WRAMX[$D000], BANK[1]");
        ValidateInstructionSetAndLabels(result);
    }

    private static void ValidateInstructionSetAndLabels(BackendResult result)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var targets = new List<string>();
        var scope = "";
        int bytes = 0;
        foreach (var raw in result.Assembly!.Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.Length == 0 || line.StartsWith("SECTION ", StringComparison.Ordinal) ||
                line.StartsWith("DEF ", StringComparison.Ordinal) || line.StartsWith("EXPORT ", StringComparison.Ordinal) ||
                line.StartsWith("ds ", StringComparison.Ordinal) || line == "PUSHS" ||
                line.StartsWith("POPS", StringComparison.Ordinal)) continue;
            if (line.EndsWith(':'))
            {
                var name = line.TrimEnd(':');
                if (!name.StartsWith('.')) scope = name;
                Assert.IsTrue(labels.Add(name.StartsWith('.') ? scope + name : name), $"Duplicate label: {scope}{name}");
                continue;
            }
            var opcode = line.Split(' ')[0];
            Assert.IsTrue(new[] { "ld", "jp", "call", "ret", "and", "or", "xor", "cpl", "sub", "sbc", "add", "adc", "cp", "rlca" }.Contains(opcode), line);
            bytes += opcode switch
            {
                "jp" or "call" => 3,
                "ld" when line.Contains("[$", StringComparison.Ordinal) => 3,
                "ld" when line.Contains("$", StringComparison.Ordinal) => 2,
                "xor" when line.Contains("$", StringComparison.Ordinal) => 2,
                _ => 1
            };
            if (opcode is "jp" or "call")
            {
                var target = line.Split(',', ' ').Last();
                targets.Add(target.StartsWith('.') ? scope + target : target);
            }
        }
        Assert.AreEqual(result.Layout!.CodeBytes, bytes);
        foreach (var target in targets) Assert.Contains(target, labels);
    }

    [TestMethod]
    public void ResourceFailuresAreLocatedAndNeverReturnPartialOutput()
    {
        var module = Lower("int x = 1; GameMemory.WriteByte(0xC100, (byte)x);");
        var cases = new[]
        {
            (new Sm83Limits(StackBytes: 1), "GBB004", "CPU stack"),
            (new Sm83Limits(StackBytes: 8193), "GBB003", "WRAM0"),
            (new Sm83Limits(WramBytes: 8, StackBytes: 2), "GBB003", "WRAM0 static slots"),
            (new Sm83Limits(CodeBytes: 1), "GBB006", "ROM0 generated code"),
            (new Sm83Limits(WramStart: 0xDFFF, WramBytes: 2), "GBB002", "DMG region limits")
        };
        foreach (var (limits, id, resource) in cases)
        {
            var result = Sm83Backend.Emit(module, limits);
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Assembly);
            Assert.IsNull(result.Layout);
            Assert.IsNull(result.MemoryReport);
            var diagnostic = result.Diagnostics.Single();
            Assert.AreEqual(id, diagnostic.Id);
            Assert.AreEqual(resource, diagnostic.Resource);
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic.Contributor));
            if (id != "GBB002") Assert.IsNotNull(diagnostic.Source);
            if (id != "GBB002") Assert.IsGreaterThan(diagnostic.AvailableBytes, diagnostic.RequiredBytes);
        }
        var invalid = Lower("");
        invalid.Functions.Single().Blocks.Single().Instructions.Add(new IrLiteral(new IrValue("bad", IrType.Bool), 4,
            new IrLocation("bad.cs", 1, 1, 0, 1)));
        Assert.AreEqual("GBB001", Sm83Backend.Emit(invalid).Diagnostics.Single().Id);
    }

    [TestMethod]
    public void VerifiedButUnknownComparisonKindFailsClosedWithSource()
    {
        var loc = new IrLocation("unknown.cs", 4, 7, 33, 1);
        var module = new IrModule("entry");
        var fn = new IrFunction("entry", IrType.Void, loc);
        var block = new IrBlock("begin", loc);
        var left = new IrValue("lhs", IrType.U8);
        var right = new IrValue("rhs", IrType.U8);
        module.Functions.Add(fn);
        fn.Blocks.Add(block);
        block.Instructions.Add(new IrLiteral(left, 1, loc));
        block.Instructions.Add(new IrLiteral(right, 2, loc));
        block.Instructions.Add(new IrCompare(new IrValue("flag", IrType.Bool), left, right, (IrCompareKind)99, false, loc));
        block.Terminate(new IrReturn(null, loc));
        IrVerifier.Verify(module); // defensively reject even a structurally verified malformed enum
        var result = Sm83Backend.Emit(module);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Assembly);
        Assert.IsNull(result.Layout);
        Assert.IsNull(result.MemoryReport);
        Assert.AreEqual("GBB005", result.Diagnostics.Single().Id);
        Assert.AreEqual(loc, result.Diagnostics.Single().Source);
    }
}
