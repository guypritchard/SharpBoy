using GB.Compiler;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Compiler.Tests;

[TestClass]
public sealed class IrTests
{
    private static IrModule Lower(string body, string extra = "") => GameIrCompiler.AnalyzeAndLower([
        ("game.cs", $"using GB.Sdk; static class Game {{ [GameEntryPoint] static void Start() {{ {body} }} {extra} }}")]);
    private static string Dump(string body, string extra = "") => IrFormatter.Format(Lower(body, extra));
    private static readonly IrLocation TestLocation = new("ir.cs", 1, 1, 0, 1);

    private static (IrModule Module, IrFunction Function, IrBlock Entry) EmptyIr(IrType result = IrType.Void)
    {
        var module = new IrModule("entry");
        var function = new IrFunction("entry", result, TestLocation);
        var entry = new IrBlock("begin", TestLocation);
        module.Functions.Add(function);
        function.Blocks.Add(entry);
        return (module, function, entry);
    }

    [TestMethod]
    public void NonFallingNonVoidControlFlowLowersWithoutDeadExits()
    {
        foreach (var method in new[]
        {
            "static int F() { while (true) { return 1; } }",
            "static int F(bool b) { if (b) return 1; else return 2; }",
            "static int F() { if (true) return 1; }",
            "static int F() { if (false) return 1; else return 2; }",
            "static int F() { if (false) return 1; return 2; }",
            "static int F() { while (false) { return 1; } return 2; }",
            "static int F() { while (true) { if (true) return 1; } }"
        })
        {
            var call = method.Contains("bool b", StringComparison.Ordinal) ? "F(true)" : "F()";
            var module = Lower($"int n = {call};", method);
            var function = module.Functions.Single(f => f.Name != module.EntryPoint);
            Assert.IsFalse(function.Blocks.Any(b => b.Terminator is IrUnreachable), method);
            IrVerifier.Verify(module);
        }
    }

    [TestMethod]
    public void TrulyFallingNonVoidMethodIsNotAccepted()
    {
        const string source = "using GB.Sdk; static class G { [GameEntryPoint] static void Start() { F(); } static int F(bool b) { if (b) return 1; } }";
        var frontend = GameFrontend.Analyze([("fall.cs", source)]);
        // C# normally refuses this at the frontend (CS0161); if accepted by a future
        // frontend, the lowerer must still refuse the reachable missing return.
        if (frontend.Success)
            Assert.ThrowsExactly<IrVerificationException>(() => GameIrCompiler.Lower(frontend));
        else
            Assert.IsTrue(frontend.Diagnostics.Any(d => d.Id == "CS0161"));
    }

    [TestMethod]
    public void VerifierChecksInstructionOrderAndBlockDominance()
    {
        var (forwardModule, _, forward) = EmptyIr();
        var v = new IrValue("v", IrType.I32);
        var later = new IrValue("later", IrType.I32);
        forward.Instructions.Add(new IrUnary(later, v, IrUnaryKind.Plus, TestLocation));
        forward.Instructions.Add(new IrLiteral(v, 3, TestLocation));
        forward.Terminate(new IrReturn(null, TestLocation));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(forwardModule));

        var (siblingModule, siblingFunction, root) = EmptyIr();
        var condition = new IrValue("condition", IrType.Bool);
        root.Instructions.Add(new IrLiteral(condition, 1, TestLocation));
        root.Terminate(new IrConditionalBranch(condition, "left", "right", TestLocation));
        var left = new IrBlock("left", TestLocation);
        var right = new IrBlock("right", TestLocation);
        var merge = new IrBlock("merge", TestLocation);
        siblingFunction.Blocks.AddRange([left, right, merge]);
        left.Instructions.Add(new IrLiteral(v, 1, TestLocation));
        left.Terminate(new IrBranch("merge", TestLocation));
        right.Terminate(new IrBranch("merge", TestLocation));
        merge.Instructions.Add(new IrUnary(later, v, IrUnaryKind.Plus, TestLocation));
        merge.Terminate(new IrReturn(null, TestLocation));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(siblingModule));

        right.Instructions.Add(new IrUnary(later, v, IrUnaryKind.Plus, TestLocation));
        merge.Instructions.Clear();
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(siblingModule));

        right.Instructions.Clear();
        left.Instructions.Clear();
        root.Instructions.Add(new IrLiteral(v, 1, TestLocation));
        merge.Instructions.Add(new IrUnary(later, v, IrUnaryKind.Plus, TestLocation));
        IrVerifier.Verify(siblingModule);

        var (loopModule, loopFunction, loopEntry) = EmptyIr();
        loopEntry.Instructions.Add(new IrLiteral(v, 1, TestLocation));
        loopEntry.Terminate(new IrBranch("loop", TestLocation));
        var loop = new IrBlock("loop", TestLocation);
        loopFunction.Blocks.Add(loop);
        loop.Instructions.Add(new IrUnary(later, v, IrUnaryKind.Plus, TestLocation));
        loop.Terminate(new IrConditionalBranch(condition, "loop", "end", TestLocation));
        var end = new IrBlock("end", TestLocation);
        loopFunction.Blocks.Add(end);
        loop.Instructions.Add(new IrLiteral(condition, 1, TestLocation));
        end.Terminate(new IrReturn(null, TestLocation));
        IrVerifier.Verify(loopModule);
    }

    [TestMethod]
    public void VerifierRefusesManuallySuppliedEntryUnreachableBlocks()
    {
        var (module, function, entry) = EmptyIr();
        entry.Terminate(new IrReturn(null, TestLocation));
        var dead = new IrBlock("dead", TestLocation);
        var deadSuccessor = new IrBlock("dead_successor", TestLocation);
        function.Blocks.AddRange([dead, deadSuccessor]);
        var value = new IrValue("v", IrType.I32);
        dead.Instructions.Add(new IrLiteral(value, 4, TestLocation));
        dead.Terminate(new IrBranch(deadSuccessor.Name, TestLocation));
        deadSuccessor.Instructions.Add(new IrUnary(new IrValue("w", IrType.I32), value, IrUnaryKind.Plus, TestLocation));
        deadSuccessor.Terminate(new IrUnreachable(TestLocation));
        StringAssert.Contains(Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module)).Message,
            "entry-unreachable block");
        deadSuccessor.Instructions.Clear();
        StringAssert.Contains(Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module)).Message,
            "entry-unreachable block");
        var invalid = EmptyIr();
        invalid.Entry.Terminate(new IrUnreachable(TestLocation));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(invalid.Module));
    }

    [TestMethod]
    public void VerifierRefusesEntryReachableDirectAndMutualRecursion()
    {
        var (module, _, entry) = EmptyIr();
        var helper = new IrFunction("helper", IrType.Void, TestLocation);
        var other = new IrFunction("other", IrType.Void, TestLocation);
        module.Functions.AddRange([helper, other]);
        var helperBlock = new IrBlock("begin", TestLocation);
        var otherBlock = new IrBlock("begin", TestLocation);
        helper.Blocks.Add(helperBlock);
        other.Blocks.Add(otherBlock);
        entry.Instructions.Add(new IrCall(null, helper, [], TestLocation));
        entry.Terminate(new IrReturn(null, TestLocation));
        helperBlock.Instructions.Add(new IrCall(null, helper, [], TestLocation));
        helperBlock.Terminate(new IrReturn(null, TestLocation));
        otherBlock.Instructions.Add(new IrCall(null, other, [], TestLocation));
        otherBlock.Terminate(new IrReturn(null, TestLocation));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
        helperBlock.Instructions.Clear();
        helperBlock.Instructions.Add(new IrCall(null, other, [], TestLocation));
        otherBlock.Instructions.Clear();
        otherBlock.Instructions.Add(new IrCall(null, helper, [], TestLocation));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
        helperBlock.Instructions.Clear();
        IrVerifier.Verify(module); // unreachable recursion in other is not entry-reachable
    }

    [TestMethod]
    public void VerifierChecksCanonicalGlobalAndConstantInitializers()
    {
        var (module, _, entry) = EmptyIr();
        entry.Terminate(new IrReturn(null, TestLocation));
        foreach (var type in new[] { IrType.Bool, IrType.U8, IrType.I8, IrType.U16, IrType.I16, IrType.I32 })
        {
            var maximum = type switch
            {
                IrType.Bool => 1, IrType.U8 or IrType.I8 => 255,
                IrType.U16 or IrType.I16 => 65535, _ => int.MaxValue
            };
            module.Globals.Add(new IrGlobal("g", type, maximum, TestLocation));
            module.Constants.Add(new IrConstant("c", type, maximum, TestLocation));
            IrVerifier.Verify(module);
            module.Globals.Clear();
            module.Constants.Clear();
            if (type == IrType.I32) continue; // every int bit pattern is valid
            foreach (var invalid in new[] { -1, maximum + 1 })
            {
                module.Globals.Add(new IrGlobal("g", type, invalid, TestLocation));
                Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
                module.Globals.Clear();
                module.Constants.Add(new IrConstant("c", type, invalid, TestLocation));
                Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
                module.Constants.Clear();
            }
        }
        module.Globals.Add(new IrGlobal("g", IrType.Void, 0, TestLocation));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(module));
    }

    [TestMethod]
    public void BranchesLoopsAndEarlyReturnsHaveCompleteCfg()
    {
        var module = Lower("int x = 0; while (x < 4) { while (x < 2) { x++; if (x == 1) continue; break; } if (x > 2) return; x++; } if (x == 4) x = 0; else x = 1;");
        var blocks = module.Functions.Single().Blocks;
        Assert.IsGreaterThan(10, blocks.Count);
        Assert.IsTrue(blocks.All(b => b.Terminator is not null));
        Assert.IsGreaterThanOrEqualTo(4, blocks.Count(b => b.Terminator is IrConditionalBranch));
        IrVerifier.Verify(module);
    }

    [TestMethod]
    public void ByteArithmeticPromotesToIntAndTruncatesOnce()
    {
        var text = Dump("byte b = 255; b++; b += 2; b = unchecked((byte)(b + 256));");
        StringAssert.Contains(text, "zeroextend");
        StringAssert.Contains(text, ":I32 = add");
        StringAssert.Contains(text, ":U8 = convert truncate");
        StringAssert.Contains(text, "unchecked");
    }

    [TestMethod]
    public void UncheckedExpressionBodiesAndBlockConversionsRetainLocationAndOverflow()
    {
        const string source = "using GB.Sdk; static class G { [GameEntryPoint] static void Start() { GameMemory.WriteByte(0xC200, Expr()); GameMemory.WriteByte(0xC201, Block()); } static byte Expr() => unchecked((byte)(255 + 2)); static byte Block() { return unchecked((byte)(255 + 2)); } }";
        var module = GameIrCompiler.AnalyzeAndLower([("unchecked.cs", source)]);
        foreach (var function in module.Functions.Where(f => f.ReturnType == IrType.U8))
        {
            var operations = function.Blocks.SelectMany(b => b.Instructions).ToArray();
            Assert.IsTrue(operations.OfType<IrConvert>().Any(c => c.Kind == IrConversionKind.Truncate), function.Name);
            Assert.IsTrue(operations.All(i => i.Source.Path == "unchecked.cs" && i.Source.Length > 0));
        }
        var expr = module.Functions.Single(f => f.Name.Contains("Expr", StringComparison.Ordinal));
        Assert.IsTrue(expr.Blocks.SelectMany(b => b.Instructions).Any(i => i.Source.Length >= "unchecked((byte)(255 + 2))".Length));
        Assert.ThrowsExactly<InvalidOperationException>(() => GameIrCompiler.AnalyzeAndLower([("checked.cs", source.Replace("unchecked", "checked", StringComparison.Ordinal))]));
    }

    [TestMethod]
    public void SignedUnsignedComparisonsAndPromotionAreExplicit()
    {
        var text = Dump("short s = -1; ushort u = 1; int i = -1; bool a = s < 0; bool b = u > 0; bool c = i < u; bool d = true == false;");
        Assert.IsGreaterThanOrEqualTo(3, text.Split("cmp.less.signed", StringSplitOptions.None).Length);
        StringAssert.Contains(text, "cmp.greater.signed"); // ushort compares as promoted int
        StringAssert.Contains(text, "cmp.equal.unsigned");
        StringAssert.Contains(text, "signextend");
        StringAssert.Contains(text, "zeroextend");
    }

    [TestMethod]
    public void IntOverflowAndNarrowSignedConversionRemainVisible()
    {
        var text = Dump("int i = 2147483647; i = i + 1; short s = unchecked((short)i); int n = s; sbyte b = unchecked((sbyte)i); int m = b;");
        StringAssert.Contains(text, "const 2147483647");
        StringAssert.Contains(text, ":I32 = add");
        StringAssert.Contains(text, ":I16 = convert truncate");
        StringAssert.Contains(text, ":I8 = convert truncate");
        StringAssert.Contains(text, "signextend");
    }

    [TestMethod]
    public void ShortCircuitRhsIsConfinedToDedicatedBlock()
    {
        var module = Lower("bool a = false && Touch(1); bool b = true || Touch(2);",
            "static bool Touch(byte x) { GameMemory.WriteByte(0xC000, x); return true; }");
        var blocks = module.Functions.Single(f => f.Name.Contains("Start", StringComparison.Ordinal)).Blocks;
        Assert.AreEqual(2, blocks.Count(b => b.Name.Contains("short_rhs", StringComparison.Ordinal)));
        Assert.AreEqual(2, blocks.Count(b => b.Terminator is IrConditionalBranch));
        Assert.IsTrue(blocks.Where(b => b.Instructions.OfType<IrCall>().Any()).All(b => b.Name.Contains("short_rhs", StringComparison.Ordinal)));
        IrVerifier.Verify(module);
    }

    [TestMethod]
    public void ConstantDeadNestedShortCircuitPrunesOnlyUnreachableBlocks()
    {
        const string source = "if (false) { bool z = true & (true && true); }";
        var module = Lower(source);
        var blocks = module.Functions.Single().Blocks;
        CollectionAssert.AreEqual(new[] { "b0_entry", "b2_else", "b6_join" },
            blocks.Select(b => b.Name).ToArray());
        Assert.IsFalse(blocks.Any(b => b.Name.Contains("short_", StringComparison.Ordinal) ||
            b.Name.Contains("then", StringComparison.Ordinal)));
        Assert.IsTrue(blocks.All(b => b.Terminator is not IrUnreachable));
        Assert.IsTrue(blocks[0].Terminator is IrBranch { Target: "b2_else" });
        Assert.IsTrue(blocks[1].Terminator is IrBranch { Target: "b6_join" });
        IrVerifier.Verify(module);
        var first = IrFormatter.Format(module, true);
        Assert.AreEqual(first, IrFormatter.Format(Lower(source), true));
        StringAssert.Contains(first, "b0_entry");
    }

    [TestMethod]
    public void DeadExitsAfterReturnAndConstantLoopsArePruned()
    {
        var afterReturn = Lower("return; bool z = true & (true && true);");
        CollectionAssert.AreEqual(new[] { "b0_entry" },
            afterReturn.Functions.Single().Blocks.Select(b => b.Name).ToArray());
        IrVerifier.Verify(afterReturn);

        var module = Lower("int n = F();", "static int F() { while (true) { return 1; } }");
        var function = module.Functions.Single(f => f.Name != module.EntryPoint);
        Assert.IsFalse(function.Blocks.Any(b => b.Name.Contains("while_exit", StringComparison.Ordinal)));
        Assert.IsTrue(function.Blocks.All(b => b.Terminator is not IrUnreachable));
        IrVerifier.Verify(module);

        var deadBody = Lower("while (false) { bool z = true & (true && true); } return;");
        Assert.IsFalse(deadBody.Functions.Single().Blocks.Any(b => b.Name.Contains("while_body", StringComparison.Ordinal) ||
            b.Name.Contains("short_", StringComparison.Ordinal)));
        IrVerifier.Verify(deadBody);
    }

    [TestMethod]
    public void EagerBooleanOperatorsEvaluateBothOperands()
    {
        var module = Lower("bool a = Touch(1) & Touch(2); bool b = Touch(3) | Touch(4);",
            "static bool Touch(byte x) { GameMemory.WriteByte(0xC000, x); return true; }");
        var function = module.Functions.Single(f => f.Name.Contains("Start", StringComparison.Ordinal));
        Assert.AreEqual(4, function.Blocks.SelectMany(b => b.Instructions).OfType<IrCall>().Count());
        Assert.AreEqual(0, function.Blocks.Count(b => b.Terminator is IrConditionalBranch));
        Assert.AreEqual(2, function.Blocks.SelectMany(b => b.Instructions).OfType<IrBinary>().Count());
    }

    [TestMethod]
    public void ArgumentsAndNestedCallsFollowSourceOrder()
    {
        var module = Lower("int a = Combine(Touch(1), Touch(2));",
            "static int Touch(byte n) { GameMemory.WriteByte(0xC000, n); return n; } static int Combine(int a, int b) => a - b;");
        var instructions = module.Functions.Single(f => f.Name.Contains("Start", StringComparison.Ordinal)).Blocks.SelectMany(b => b.Instructions).ToArray();
        var calls = instructions.OfType<IrCall>().ToArray();
        Assert.HasCount(3, calls);
        Assert.IsTrue(calls[0].Target.Name.Contains("Touch", StringComparison.Ordinal));
        Assert.IsTrue(calls[1].Target.Name.Contains("Touch", StringComparison.Ordinal));
        Assert.IsTrue(calls[2].Target.Name.Contains("Combine", StringComparison.Ordinal));
        Assert.AreEqual(calls[0].Value, calls[2].Arguments[0]);
        Assert.AreEqual(calls[1].Value, calls[2].Arguments[1]);
    }

    [TestMethod]
    public void NamedArgumentsKeepSourceEvaluationOrderButParameterBindingOrder()
    {
        var module = Lower("int answer = Combine(b: Touch(2), a: Touch(1));",
            "static int Touch(byte n) { GameMemory.WriteByte(0xC000, n); return n; } static int Combine(int a, int b) => a - b;");
        var instructions = module.Functions.Single(f => f.Name.Contains("Start", StringComparison.Ordinal)).Blocks.Single().Instructions;
        var calls = instructions.OfType<IrCall>().ToArray();
        Assert.HasCount(3, calls);
        Assert.AreEqual(calls[1].Value, calls[2].Arguments[0]);
        Assert.AreEqual(calls[0].Value, calls[2].Arguments[1]);
        var literals = instructions.OfType<IrLiteral>().Where(i => i.Bits is 1 or 2).ToArray();
        Assert.HasCount(2, literals);
        Assert.AreEqual(2, literals[0].Bits);
        Assert.AreEqual(1, literals[1].Bits);
    }

    [TestMethod]
    public void DecrementAndNestedShortCircuitRetainControlDependencies()
    {
        var module = Lower("int i = 2; --i; bool x = (Hit(1) && Hit(2)) || Hit(3);",
            "static bool Hit(byte n) { GameMemory.WriteByte(0xC000, n); return true; }");
        var function = module.Functions.Single(f => f.Name.Contains("Start", StringComparison.Ordinal));
        Assert.IsTrue(function.Blocks.SelectMany(b => b.Instructions).OfType<IrBinary>().Any(b => b.Kind == IrBinaryKind.Subtract));
        Assert.AreEqual(3, function.Blocks.SelectMany(b => b.Instructions).OfType<IrCall>().Count());
        Assert.AreEqual(2, function.Blocks.Count(b => b.Terminator is IrConditionalBranch));
        Assert.IsTrue(function.Blocks.Where(b => b.Instructions.OfType<IrCall>().Any()).All(b => b.Name.Contains("short_rhs", StringComparison.Ordinal) || b.Name == "b0_entry"));
        IrVerifier.Verify(module);
    }

    [TestMethod]
    public void CompoundAndPostIncrementObserveOriginalValueAndStoreOnce()
    {
        var module = Lower("byte x = 4; int a = x++ + x; x += 3;");
        var instructions = module.Functions.Single().Blocks.Single().Instructions;
        Assert.AreEqual(4, instructions.OfType<IrStore>().Count()); // x init, x++, a init, x +=
        var stores = instructions.OfType<IrStore>().ToArray();
        Assert.AreEqual(stores[0].Slot, stores[1].Slot);
        Assert.AreNotEqual(stores[1].Slot, stores[2].Slot);
        Assert.AreEqual(stores[1].Slot, stores[3].Slot);
        Assert.IsGreaterThanOrEqualTo(3, instructions.OfType<IrBinary>().Count());
    }

    [TestMethod]
    public void GlobalsRetainInitializerConstantsInlineAndSdkIntrinsicIsObservable()
    {
        var module = Lower("Value += Seed; GameMemory.WriteByte(0xC000, Value);",
            "static byte Value = 7; const byte Seed = 5; static short Unused = -2;");
        Assert.HasCount(2, module.Globals);
        Assert.HasCount(1, module.Constants);
        Assert.AreEqual(7, module.Globals.Single(g => g.Name.Contains("Value", StringComparison.Ordinal)).InitialValue);
        Assert.AreEqual(65534, module.Globals.Single(g => g.Name.Contains("Unused", StringComparison.Ordinal)).InitialValue);
        var instructions = module.Functions.Single().Blocks.SelectMany(b => b.Instructions).ToArray();
        Assert.IsTrue(instructions.OfType<IrLiteral>().Any(l => l.Bits == 5));
        Assert.AreEqual(1, instructions.OfType<IrWriteByte>().Count());
        Assert.AreEqual(IrEffect.HardwareWrite, instructions.OfType<IrWriteByte>().Single().Effect);
        Assert.IsFalse(instructions.OfType<IrCall>().Any());
    }

    [TestMethod]
    public void SameInputInAnyFileOrderProducesIdenticalText()
    {
        (string Path, string Text) a = ("a.cs", "using GB.Sdk; static class Game { [GameEntryPoint] static void Start() { Other.Run(); } }");
        (string Path, string Text) b = ("b.cs", "static class Other { static byte value = 3; public static void Run() { value++; } }");
        Assert.AreEqual(IrFormatter.Format(GameIrCompiler.AnalyzeAndLower([a, b]), true),
            IrFormatter.Format(GameIrCompiler.AnalyzeAndLower([b, a]), true));
    }

    [TestMethod]
    public void SourceLocationsIncludeDeclarationInstructionAndTerminatorSpans()
    {
        var module = GameIrCompiler.AnalyzeAndLower([("source.cs", "using GB.Sdk; static class G { [GameEntryPoint] static void Start() { int x = 1; if (x > 0) return; } }")]);
        var function = module.Functions.Single();
        Assert.AreEqual("source.cs", function.Source.Path);
        Assert.IsGreaterThan(0, function.Source.Length);
        Assert.IsTrue(function.Locals.All(s => s.Source.Line == 1 && s.Source.Column > 0));
        Assert.IsTrue(function.Blocks.SelectMany(b => b.Instructions).All(i => i.Source.Path == "source.cs" && i.Source.Length > 0));
        Assert.IsTrue(function.Blocks.All(b => b.Terminator!.Source.Line == 1));
        StringAssert.Contains(IrFormatter.Format(module, true), "@ source.cs:1:");
    }

    [TestMethod]
    public void SpoofedGameMemoryIsAnOrdinaryDirectCall()
    {
        var module = GameIrCompiler.AnalyzeAndLower([("spoof.cs", "using GB.Sdk; static class GameMemory { public static void WriteByte(ushort a, byte b) {} } static class G { [GameEntryPoint] static void Start() { GameMemory.WriteByte(1, 2); } }")]);
        Assert.AreEqual(1, module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IrCall>().Count());
        Assert.AreEqual(0, module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IrWriteByte>().Count());
    }

    [TestMethod]
    public void InvalidFrontendCannotLower()
    {
        var invalid = GameFrontend.Analyze([("bad.cs", "using GB.Sdk; static class G { [GameEntryPoint] static void Start() { for (;;) break; } }")]);
        Assert.IsFalse(invalid.Success);
        Assert.ThrowsExactly<InvalidOperationException>(() => GameIrCompiler.Lower(invalid));
        Assert.ThrowsExactly<InvalidOperationException>(() => GameIrCompiler.AnalyzeAndLower([("bad.cs", "invalid C#") ]));
    }

    [TestMethod]
    public void VerifierRejectsMalformedBlocksCallsSlotsReturnsAndIntrinsics()
    {
        var at = new IrLocation("bad.cs", 1, 1, 0, 1);
        IrModule Fresh()
        {
            var module = new IrModule("f");
            module.Functions.Add(new IrFunction("f", IrType.Void, at));
            module.Functions[0].Blocks.Add(new IrBlock("entry", at));
            return module;
        }
        var missing = Fresh();
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(missing));
        missing.Functions[0].Blocks[0].Terminate(new IrBranch("absent", at));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(missing));
        Assert.ThrowsExactly<IrVerificationException>(() => missing.Functions[0].Blocks[0].Terminate(new IrReturn(null, at)));
        var badReturn = Fresh();
        badReturn.Functions[0].Blocks[0].Terminate(new IrReturn(new IrValue("x", IrType.Bool), at));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(badReturn));
        var badSlot = Fresh();
        badSlot.Functions[0].Blocks[0].Instructions.Add(new IrLoad(new IrValue("v0", IrType.I32),
            new IrSlot("alien", IrType.I32, IrSlotKind.Local, at), null, at));
        badSlot.Functions[0].Blocks[0].Terminate(new IrReturn(null, at));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(badSlot));
        var badWrite = Fresh();
        badWrite.Functions[0].Blocks[0].Instructions.Add(new IrWriteByte(new IrValue("address", IrType.I32), new IrValue("byte", IrType.U8), at));
        badWrite.Functions[0].Blocks[0].Terminate(new IrReturn(null, at));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(badWrite));
        var badCall = Fresh();
        badCall.Functions[0].Blocks[0].Instructions.Add(new IrCall(null, badCall.Functions[0], [new IrValue("arg", IrType.U8)], at));
        badCall.Functions[0].Blocks[0].Terminate(new IrReturn(null, at));
        Assert.ThrowsExactly<IrVerificationException>(() => IrVerifier.Verify(badCall));
    }
}
