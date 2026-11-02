using GB.Sdk;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GB.Compiler;

/// <summary>Lower only a successfully validated frontend result; never execute submitted code.</summary>
public static class GameIrCompiler
{
    public static IrModule AnalyzeAndLower(IEnumerable<(string Path, string Text)> sources) => Lower(GameFrontend.Analyze(sources));

    public static IrModule Lower(FrontendResult frontend)
    {
        if (!frontend.Success || frontend.Compilation is null || frontend.EntryPoint is null)
            throw new InvalidOperationException("Cannot lower source with frontend diagnostics or without validated semantic context: " +
                string.Join("; ", frontend.Diagnostics));
        var compilation = frontend.Compilation;
        var methods = frontend.ReachableCalls.Keys.OrderBy(Key, StringComparer.Ordinal).ToArray();
        if (!methods.Contains(frontend.EntryPoint, SymbolEqualityComparer.Default))
            throw new InvalidOperationException("Validated entry is missing from the reachable graph.");
        var module = new IrModule(Key(frontend.EntryPoint));
        var functionMap = new Dictionary<IMethodSymbol, IrFunction>(SymbolEqualityComparer.Default);
        foreach (var method in methods)
        {
            var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();
            var function = new IrFunction(Key(method), Type(method.ReturnType), IrLocation.From(declaration.GetLocation()));
            foreach (var parameter in method.Parameters)
                function.Parameters.Add(new IrSlot($"p{parameter.Ordinal}_{parameter.Name}", Type(parameter.Type),
                    IrSlotKind.Parameter, IrLocation.From(parameter.Locations[0])));
            module.Functions.Add(function);
            functionMap.Add(method, function);
        }
        var globals = new Dictionary<IFieldSymbol, IrGlobal>(SymbolEqualityComparer.Default);
        var constants = new Dictionary<IFieldSymbol, IrConstant>(SymbolEqualityComparer.Default);
        // Include all validated fields (even unreachable) in stable qualified-name order. Static initializers
        // are compile-time constants; no initialization code or implicit static constructor is generated.
        var fields = compilation.SyntaxTrees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
            .Select(node => (Node: node, Symbol: compilation.GetSemanticModel(node.SyntaxTree).GetDeclaredSymbol(node) as IFieldSymbol))
            .Where(pair => pair.Symbol is not null).OrderBy(pair => Key(pair.Symbol!), StringComparer.Ordinal);
        foreach (var (node, symbolMaybe) in fields)
        {
            var symbol = symbolMaybe!;
            var name = Key(symbol);
            var value = Bits(symbol.ConstantValue ?? (node.Initializer is not null
                ? compilation.GetSemanticModel(node.SyntaxTree).GetConstantValue(node.Initializer.Value).Value : null), Type(symbol.Type));
            if (symbol.IsConst)
            {
                var constant = new IrConstant(name, Type(symbol.Type), value, IrLocation.From(node.GetLocation()));
                module.Constants.Add(constant);
                constants.Add(symbol, constant);
            }
            else
            {
                var global = new IrGlobal(name, Type(symbol.Type), value, IrLocation.From(node.GetLocation()));
                module.Globals.Add(global);
                globals.Add(symbol, global);
            }
        }
        foreach (var method in methods)
            new BodyLowerer(compilation, functionMap[method], functionMap, globals, constants).Lower(method);
        IrVerifier.Verify(module);
        return module;
    }

    internal static string Key(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    internal static IrType Type(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Void => IrType.Void, SpecialType.System_Boolean => IrType.Bool,
        SpecialType.System_Byte => IrType.U8, SpecialType.System_SByte => IrType.I8,
        SpecialType.System_UInt16 => IrType.U16, SpecialType.System_Int16 => IrType.I16,
        SpecialType.System_Int32 => IrType.I32,
        _ => throw new IrVerificationException($"unsupported validated type {type}")
    };
    internal static int Bits(object? value, IrType type) => value is null ? 0 : type switch
    {
        IrType.Bool => (bool)value ? 1 : 0,
        IrType.U8 or IrType.I8 => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) & 0xff,
        IrType.U16 or IrType.I16 => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) & 0xffff,
        IrType.I32 => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new IrVerificationException("void constant")
    };

    private sealed class BodyLowerer(
        CSharpCompilation compilation, IrFunction function,
        Dictionary<IMethodSymbol, IrFunction> functions, Dictionary<IFieldSymbol, IrGlobal> globals,
        Dictionary<IFieldSymbol, IrConstant> constants)
    {
        private readonly Dictionary<ILocalSymbol, IrSlot> locals = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<IParameterSymbol, IrSlot> parameters = new(SymbolEqualityComparer.Default);
        private readonly Stack<(IrBlock Exit, IrBlock Continue)> loops = new();
        private IrBlock current = null!;
        private int nextBlock, nextValue, nextSlot;

        private IrBlock Block(string label, Location location)
        {
            var block = new IrBlock($"b{nextBlock++}_{label}", IrLocation.From(location));
            function.Blocks.Add(block);
            return block;
        }
        private IrValue Value(IrType type) => new($"v{nextValue++}", type);
        private IrValue Emit(IrInstruction instruction) { current.Instructions.Add(instruction); return instruction.Result!; }
        private static IrLocation At(IOperation operation) => IrLocation.From(operation.Syntax.GetLocation());
        private static IrLocation At(SyntaxNode node) => IrLocation.From(node.GetLocation());
        private void Jump(IrBlock target, Location location) => current.Terminate(new IrBranch(target.Name, IrLocation.From(location)));

        public void Lower(IMethodSymbol method)
        {
            foreach (var parameter in method.Parameters) parameters.Add(parameter, function.Parameters[parameter.Ordinal]);
            var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax();
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            current = Block("entry", declaration.Identifier.GetLocation());
            if (declaration.Body is not null) Statement(RequiredOperation(model, declaration.Body));
            else if (declaration.ExpressionBody is not null)
            {
                var syntax = declaration.ExpressionBody.Expression;
                var value = Expression(RequiredOperation(model, syntax), At(syntax));
                if (function.ReturnType != IrType.Void)
                    value = Convert(value, function.ReturnType, At(syntax)); // expression-body implicit return conversion
                current.Terminate(new IrReturn(function.ReturnType == IrType.Void ? null : value, At(declaration.ExpressionBody)));
            }
            if (current.Terminator is null)
            {
                // An exit can exist syntactically even when every incoming edge is impossible
                // (e.g. the false edge of while (true), or a dead if arm).
                var reachable = ReachableBlocks();
                if (reachable.Contains(current))
                {
                    if (function.ReturnType != IrType.Void)
                        throw new IrVerificationException($"{function.Name}: non-void fallthrough");
                    current.Terminate(new IrReturn(null, At(declaration)));
                }
            }
            // Only entry-reachable blocks belong to the emitted CFG. Remove dead arms
            // after construction, preserving the original order and generated names.
            var live = ReachableBlocks();
            function.Blocks.RemoveAll(block => !live.Contains(block));
            foreach (var block in function.Blocks.Where(b => b.Terminator is null))
                throw new IrVerificationException($"{function.Name}/{block.Name}: reachable unterminated block");
        }

        private IOperation RequiredOperation(SemanticModel model, SyntaxNode syntax)
        {
            var operation = model.GetOperation(syntax);
            if (operation is not null) return operation;
            // Roslyn can omit the outer IOperation for an expression-bodied unchecked(...).
            // The inner operation is still bound in the unchecked context, so its conversion
            // and constant values retain C# unchecked semantics. Never peel checked(...).
            if (syntax is CheckedExpressionSyntax { Keyword.RawKind: (int)SyntaxKind.UncheckedKeyword } uncheckedSyntax)
                return RequiredOperation(model, uncheckedSyntax.Expression);
            if (syntax is ParenthesizedExpressionSyntax parenthesized)
                return RequiredOperation(model, parenthesized.Expression);
            throw new IrVerificationException($"{function.Name}: Roslyn did not provide an operation at {At(syntax)} for '{syntax}'.");
        }

        private HashSet<IrBlock> ReachableBlocks()
        {
            var byName = function.Blocks.ToDictionary(b => b.Name, StringComparer.Ordinal);
            var visited = new HashSet<IrBlock>();
            var pending = new Stack<IrBlock>();
            pending.Push(function.Blocks[0]);
            while (pending.Count != 0)
            {
                var block = pending.Pop();
                if (!visited.Add(block)) continue;
                switch (block.Terminator)
                {
                    case IrBranch branch: pending.Push(byName[branch.Target]); break;
                    case IrConditionalBranch branch:
                        pending.Push(byName[branch.TrueTarget]);
                        pending.Push(byName[branch.FalseTarget]);
                        break;
                }
            }
            return visited;
        }

        private void Statement(IOperation operation)
        {
            if (current.Terminator is not null) return;
            switch (operation)
            {
                case IBlockOperation block:
                    foreach (var statement in block.Operations) Statement(statement);
                    break;
                case IVariableDeclarationGroupOperation group:
                    foreach (var declaration in group.Declarations) Statement(declaration);
                    break;
                case IVariableDeclarationOperation declaration:
                    foreach (var declarator in declaration.Declarators) Statement(declarator);
                    break;
                case IVariableDeclaratorOperation variable:
                    var slot = new IrSlot($"l{nextSlot++}_{variable.Symbol.Name}", Type(variable.Symbol.Type),
                        IrSlotKind.Local, At(variable));
                    function.Locals.Add(slot);
                    locals.Add(variable.Symbol, slot);
                    if (variable.Initializer is not null)
                        Store(slot, null, Expression(variable.Initializer.Value), At(variable));
                    break;
                case IExpressionStatementOperation expression: Expression(expression.Operation); break;
                case IReturnOperation ret:
                    // Expression lowering may move current to a short-circuit join block.
                    var returned = ret.ReturnedValue is null ? null : Expression(ret.ReturnedValue);
                    current.Terminate(new IrReturn(returned, At(ret)));
                    break;
                case IConditionalOperation conditional when conditional.Syntax is IfStatementSyntax:
                    var thenBlock = Block("then", conditional.WhenTrue.Syntax.GetLocation());
                    var elseBlock = Block("else", (conditional.WhenFalse ?? conditional).Syntax.GetLocation());
                    var condition = Expression(conditional.Condition);
                    Conditional(condition, conditional.Condition, thenBlock, elseBlock);
                    current = thenBlock;
                    Statement(conditional.WhenTrue);
                    var thenEnd = current;
                    current = elseBlock;
                    if (conditional.WhenFalse is not null) Statement(conditional.WhenFalse);
                    var elseEnd = current;
                    if (thenEnd.Terminator is null || elseEnd.Terminator is null)
                    {
                        var join = Block("join", conditional.Syntax.GetLocation());
                        if (thenEnd.Terminator is null) { current = thenEnd; Jump(join, conditional.Syntax.GetLocation()); }
                        if (elseEnd.Terminator is null) { current = elseEnd; Jump(join, conditional.Syntax.GetLocation()); }
                        current = join;
                    }
                    break;
                case IWhileLoopOperation loop:
                    var head = Block("while_test", loop.Syntax.GetLocation());
                    var body = Block("while_body", loop.Body.Syntax.GetLocation());
                    var exit = Block("while_exit", loop.Syntax.GetLocation());
                    Jump(head, loop.Syntax.GetLocation());
                    current = head;
                    var test = Expression(loop.Condition!);
                    Conditional(test, loop.Condition!, body, exit);
                    loops.Push((exit, head));
                    current = body;
                    Statement(loop.Body);
                    if (current.Terminator is null) Jump(head, loop.Body.Syntax.GetLocation());
                    loops.Pop();
                    current = exit;
                    break;
                case IBranchOperation branch:
                    var target = branch.BranchKind == BranchKind.Break ? loops.Peek().Exit : loops.Peek().Continue;
                    Jump(target, branch.Syntax.GetLocation());
                    break;
                default: throw new IrVerificationException($"unsupported validated statement {operation.Kind} at {At(operation)}");
            }
        }

        private void Conditional(IrValue value, IOperation condition, IrBlock whenTrue, IrBlock whenFalse)
        {
            // Preserve the evaluation of the condition, but do not create a fictitious
            // CFG edge for a compile-time constant (including parenthesized constants).
            if (condition.ConstantValue.HasValue && condition.ConstantValue.Value is bool known)
                current.Terminate(new IrBranch((known ? whenTrue : whenFalse).Name, At(condition)));
            else
                current.Terminate(new IrConditionalBranch(value, whenTrue.Name, whenFalse.Name, At(condition)));
        }

        private (IrSlot? Slot, IrGlobal? Global, IrType Type) Place(IOperation target) => target switch
        {
            ILocalReferenceOperation local => (locals[local.Local], null, Type(local.Type!)),
            IParameterReferenceOperation parameter => (parameters[parameter.Parameter], null, Type(parameter.Type!)),
            IFieldReferenceOperation field when !field.Field.IsConst => (null, globals[field.Field], Type(field.Type!)),
            _ => throw new IrVerificationException($"unsupported validated assignment target {target.Kind}")
        };
        private IrValue Load(IrSlot? slot, IrGlobal? global, IrType type, IrLocation location) =>
            Emit(new IrLoad(Value(type), slot, global, location));
        private void Store(IrSlot? slot, IrGlobal? global, IrValue value, IrLocation location) =>
            current.Instructions.Add(new IrStore(value, slot, global, location));
        private IrValue Literal(int bits, IrType type, IrLocation location) => Emit(new IrLiteral(Value(type), bits, location));
        private IrValue Convert(IrValue input, IrType target, IrLocation location)
        {
            if (input.Type == target) return input;
            var from = IrVerifier.Width(input.Type);
            var to = IrVerifier.Width(target);
            var kind = from > to ? IrConversionKind.Truncate : from < to ?
                IrVerifier.Signed(input.Type) ? IrConversionKind.SignExtend : IrConversionKind.ZeroExtend :
                IrConversionKind.Reinterpret;
            return Emit(new IrConvert(Value(target), input, kind, location));
        }
        private static IrBinaryKind Binary(BinaryOperatorKind kind) => kind switch
        {
            BinaryOperatorKind.Add => IrBinaryKind.Add, BinaryOperatorKind.Subtract => IrBinaryKind.Subtract,
            BinaryOperatorKind.And => IrBinaryKind.And, BinaryOperatorKind.Or => IrBinaryKind.Or,
            BinaryOperatorKind.ExclusiveOr => IrBinaryKind.Xor,
            _ => throw new IrVerificationException($"unsupported validated binary {kind}")
        };
        private static IrCompareKind Compare(BinaryOperatorKind kind) => kind switch
        {
            BinaryOperatorKind.Equals => IrCompareKind.Equal, BinaryOperatorKind.NotEquals => IrCompareKind.NotEqual,
            BinaryOperatorKind.LessThan => IrCompareKind.Less, BinaryOperatorKind.LessThanOrEqual => IrCompareKind.LessEqual,
            BinaryOperatorKind.GreaterThan => IrCompareKind.Greater,
            BinaryOperatorKind.GreaterThanOrEqual => IrCompareKind.GreaterEqual,
            _ => throw new IrVerificationException($"unsupported validated comparison {kind}")
        };

        private IrValue Expression(IOperation operation, IrLocation? sourceOverride = null)
        {
            var location = sourceOverride ?? At(operation);
            switch (operation)
            {
                case IParenthesizedOperation paren: return Expression(paren.Operand);
                case IConversionOperation conversion:
                    return Convert(Expression(conversion.Operand), Type(conversion.Type!), location);
                case ILiteralOperation literal: return Literal(Bits(literal.ConstantValue.Value, Type(literal.Type!)), Type(literal.Type!), location);
                case ILocalReferenceOperation local:
                    return Load(locals[local.Local], null, Type(local.Type!), location);
                case IParameterReferenceOperation parameter:
                    return Load(parameters[parameter.Parameter], null, Type(parameter.Type!), location);
                case IFieldReferenceOperation field when field.Field.IsConst:
                    var constant = constants[field.Field];
                    return Literal(constant.Value, constant.Type, location);
                case IFieldReferenceOperation field:
                    return Load(null, globals[field.Field], Type(field.Type!), location);
                case ISimpleAssignmentOperation assignment:
                    var place = Place(assignment.Target);
                    var assigned = Expression(assignment.Value);
                    Store(place.Slot, place.Global, assigned, location);
                    return assigned;
                case ICompoundAssignmentOperation compound:
                    var destination = Place(compound.Target);
                    var previous = Load(destination.Slot, destination.Global, destination.Type, location);
                    var left = Promote(previous, destination.Type == IrType.Bool ? IrType.Bool : IrType.I32, location);
                    var right = Expression(compound.Value);
                    var calculation = Emit(new IrBinary(Value(left.Type), left, Convert(right, left.Type, location), Binary(compound.OperatorKind), location));
                    var narrowed = Convert(calculation, destination.Type, location);
                    Store(destination.Slot, destination.Global, narrowed, location);
                    return narrowed;
                case IIncrementOrDecrementOperation increment:
                    var incrementPlace = Place(increment.Target);
                    var old = Load(incrementPlace.Slot, incrementPlace.Global, incrementPlace.Type, location);
                    var promoted = Promote(old, IrType.I32, location);
                    var one = Literal(1, IrType.I32, location);
                    var updated = Emit(new IrBinary(Value(IrType.I32), promoted, one,
                        increment.Kind == OperationKind.Increment ? IrBinaryKind.Add : IrBinaryKind.Subtract, location));
                    var result = Convert(updated, old.Type, location);
                    Store(incrementPlace.Slot, incrementPlace.Global, result, location);
                    return increment.IsPostfix ? old : result;
                case IUnaryOperation unary:
                    var operand = Expression(unary.Operand);
                    return Emit(new IrUnary(Value(Type(unary.Type!)), operand, unary.OperatorKind switch
                    {
                        UnaryOperatorKind.Plus => IrUnaryKind.Plus, UnaryOperatorKind.Minus => IrUnaryKind.Negate,
                        UnaryOperatorKind.BitwiseNegation => IrUnaryKind.BitwiseNot,
                        UnaryOperatorKind.Not => IrUnaryKind.LogicalNot,
                        _ => throw new IrVerificationException($"unsupported unary {unary.OperatorKind}")
                    }, location));
                case IBinaryOperation binary when binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr:
                    var lhs = Expression(binary.LeftOperand);
                    var rhsBlock = Block("short_rhs", binary.RightOperand.Syntax.GetLocation());
                    var skipBlock = Block("short_skip", binary.Syntax.GetLocation());
                    var merge = Block("short_merge", binary.Syntax.GetLocation());
                    var temp = new IrSlot($"t{nextSlot++}_short", IrType.Bool, IrSlotKind.Temporary, location);
                    function.Locals.Add(temp);
                    var and = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd;
                    current.Terminate(new IrConditionalBranch(lhs, (and ? rhsBlock : skipBlock).Name,
                        (and ? skipBlock : rhsBlock).Name, location));
                    current = skipBlock;
                    Store(temp, null, Literal(and ? 0 : 1, IrType.Bool, location), location);
                    Jump(merge, binary.Syntax.GetLocation());
                    current = rhsBlock;
                    Store(temp, null, Expression(binary.RightOperand), location);
                    Jump(merge, binary.Syntax.GetLocation());
                    current = merge;
                    return Load(temp, null, IrType.Bool, location);
                case IBinaryOperation binary:
                    var first = Expression(binary.LeftOperand);
                    var second = Expression(binary.RightOperand);
                    if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or
                        BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
                        BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
                        return Emit(new IrCompare(Value(IrType.Bool), first, second, Compare(binary.OperatorKind),
                            IrVerifier.Signed(first.Type), location));
                    return Emit(new IrBinary(Value(Type(binary.Type!)), first, second, Binary(binary.OperatorKind), location));
                case IInvocationOperation invocation:
                    var sdk = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(compilation.References.OfType<PortableExecutableReference>()
                        .Single(r => string.Equals(r.FilePath, typeof(GameMemory).Assembly.Location, StringComparison.OrdinalIgnoreCase)))!;
                    var intrinsic = DisplayIntrinsic.Identify(invocation.TargetMethod, sdk);
                    if (intrinsic == DisplayIntrinsicKind.ReadByte)
                        return Emit(new IrReadByte(Value(IrType.U8), Expression(invocation.Arguments.Single().Value), location));
                    if (intrinsic == DisplayIntrinsicKind.WriteText)
                    {
                        var address = Expression(invocation.Arguments.Single(a => a.Parameter!.Ordinal == 0).Value);
                        var text = (string)invocation.Arguments.Single(a => a.Parameter!.Ordinal == 1).Value.ConstantValue.Value!;
                        current.Instructions.Add(new IrText(address, text, location));
                        return null!;
                    }
                    if (intrinsic == DisplayIntrinsicKind.CopyToBuffer)
                    {
                        var values = new int[4];
                        foreach (var argument in invocation.Arguments)
                        {
                            if (argument.Parameter is null || !argument.Value.ConstantValue.HasValue)
                                throw new IrVerificationException("Bank copy requires compile-time constants", location);
                            values[argument.Parameter.Ordinal] = System.Convert.ToInt32(argument.Value.ConstantValue.Value);
                        }
                        current.Instructions.Add(new IrBankCopy((byte)values[0], (ushort)values[1],
                            (ushort)values[2], (ushort)values[3], location));
                        return null!;
                    }
                    if (intrinsic == DisplayIntrinsicKind.ReadBufferByte)
                    {
                        var argument = invocation.Arguments.Single();
                        if (!argument.Value.ConstantValue.HasValue)
                            throw new IrVerificationException("Buffer read requires a compile-time constant", location);
                        return Emit(new IrBankRead(Value(IrType.U8), (ushort)System.Convert.ToInt32(argument.Value.ConstantValue.Value), location));
                    }
                    if (intrinsic is DisplayIntrinsicKind.InitializeSprites or DisplayIntrinsicKind.SetSprite or DisplayIntrinsicKind.HideSprite)
                    {
                        byte index = 0, attributes = 0;
                        var coordinates = new IrValue[2];
                        foreach (var argument in invocation.Arguments)
                        {
                            var ordinal = argument.Parameter?.Ordinal ?? throw new IrVerificationException("Missing sprite parameter", location);
                            if (ordinal == 0 && intrinsic != DisplayIntrinsicKind.InitializeSprites || ordinal == 3)
                            {
                                if (!argument.Value.ConstantValue.HasValue || argument.Value.ConstantValue.Value is not byte value ||
                                    (ordinal == 0 ? value >= 40 : (value & 15) != 0))
                                    throw new IrVerificationException("Invalid sprite index/attributes constant", location);
                                if (ordinal == 0) index = value; else attributes = value;
                            }
                            else if (intrinsic == DisplayIntrinsicKind.SetSprite)
                                coordinates[ordinal - 1] = Expression(argument.Value);
                        }
                        current.Instructions.Add(new IrSprite(intrinsic switch
                        {
                            DisplayIntrinsicKind.InitializeSprites => IrSpriteKind.Initialize,
                            DisplayIntrinsicKind.SetSprite => IrSpriteKind.Set,
                            _ => IrSpriteKind.Hide
                        }, index, attributes, intrinsic == DisplayIntrinsicKind.SetSprite ? coordinates : [], location));
                        return null!;
                    }
                    var args = new IrValue[invocation.TargetMethod.Parameters.Length];
                    // IInvocationOperation.Arguments is in evaluation order; bind the evaluated
                    // values by formal parameter ordinal (named arguments may be out of order).
                    // Synthetic/default arguments can have null Syntax, so never sort on spans.
                    foreach (var argument in invocation.Arguments)
                    {
                        if (argument.Parameter is null || argument.Parameter.Ordinal >= args.Length)
                            throw new IrVerificationException($"{function.Name}: missing invocation parameter at {At(invocation)}");
                        args[argument.Parameter.Ordinal] = Expression(argument.Value);
                    }
                    if (intrinsic == DisplayIntrinsicKind.WriteByte)
                    {
                        current.Instructions.Add(new IrWriteByte(args[0], args[1], location));
                        return null!; // void expression statements have no value to consume
                    }
                    if (intrinsic != DisplayIntrinsicKind.None)
                    {
                        current.Instructions.Add(new IrDisplay(intrinsic switch
                        {
                            DisplayIntrinsicKind.InitializeBackground => IrDisplayKind.InitializeBackground,
                            DisplayIntrinsicKind.WaitForNextVBlank => IrDisplayKind.WaitForNextVBlank,
                            DisplayIntrinsicKind.SetScroll => IrDisplayKind.SetScroll,
                            _ => throw new IrVerificationException("Unknown SDK display intrinsic")
                        }, args, location));
                        return null!;
                    }
                    if (!functions.TryGetValue(invocation.TargetMethod, out var callee))
                        throw new IrVerificationException($"unresolved validated call {invocation.TargetMethod}");
                    var returnValue = callee.ReturnType == IrType.Void ? null : Value(callee.ReturnType);
                    current.Instructions.Add(new IrCall(returnValue, callee, args, location));
                    return returnValue!;
                default: throw new IrVerificationException($"unsupported validated expression {operation.Kind} at {location}");
            }
        }

        private IrValue Promote(IrValue value, IrType type, IrLocation location) => Convert(value, type, location);

    }
}
