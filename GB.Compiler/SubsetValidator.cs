using System.Collections.Immutable;
using GB.Sdk;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GB.Compiler;

/// <summary>A source diagnostic with a stable category and one-based coordinates.</summary>
public sealed record FrontendDiagnostic(string Id, string Message, string Path, int Line, int Column)
{
    public override string ToString() => $"{Path}({Line},{Column}): error {Id}: {Message}";

    internal static FrontendDiagnostic FromRoslyn(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        return new(diagnostic.Id, diagnostic.GetMessage(), span.Path,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
    }
}

/// <summary>Semantic symbols remain available to a future lowering stage; no game code is executed.</summary>
public sealed class FrontendResult(
    IReadOnlyList<FrontendDiagnostic> diagnostics,
    IMethodSymbol? entryPoint,
    IReadOnlyDictionary<IMethodSymbol, IReadOnlyList<IMethodSymbol>> reachableCalls,
    CSharpCompilation? compilation = null)
{
    public IReadOnlyList<FrontendDiagnostic> Diagnostics { get; } = diagnostics;
    public IMethodSymbol? EntryPoint { get; } = entryPoint;
    /// <summary>Direct-call edges for methods reachable from the entry point only; unreachable submitted methods are still validated.</summary>
    public IReadOnlyDictionary<IMethodSymbol, IReadOnlyList<IMethodSymbol>> ReachableCalls { get; } = reachableCalls;
    public bool Success => Diagnostics.Count == 0;
    /// <summary>The exact compilation that was validated. Lowering refuses results with diagnostics.</summary>
    public CSharpCompilation? Compilation { get; } = compilation;
}

/// <summary>Fail-closed Roslyn/IOperation frontend over every supplied source file.</summary>
public static class GameFrontend
{
    public static FrontendResult Analyze(IEnumerable<(string Path, string Text)> sources)
    {
        var compilation = SourceValidator.CreateCompilation(sources);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .OrderBy(d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(d => d.Location.SourceSpan.Start).ToArray();
        if (errors.Length != 0)
            return new(errors.Select(FrontendDiagnostic.FromRoslyn).ToArray(), null,
                new Dictionary<IMethodSymbol, IReadOnlyList<IMethodSymbol>>(SymbolEqualityComparer.Default));
        return new Validator(compilation).Run();
    }

    private sealed class Validator(CSharpCompilation compilation)
    {
        private readonly List<FrontendDiagnostic> diagnostics = [];
        private readonly Dictionary<IMethodSymbol, Dictionary<IMethodSymbol, Location>> calls = new(SymbolEqualityComparer.Default);
        private readonly List<IMethodSymbol> entries = [];
        private readonly IAssemblySymbol sdkAssembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(
            compilation.References.OfType<PortableExecutableReference>().Single(reference =>
                string.Equals(reference.FilePath, typeof(GameMemory).Assembly.Location, StringComparison.OrdinalIgnoreCase)))!;
        private INamedTypeSymbol? EntryAttribute => sdkAssembly.GetTypeByMetadataName(typeof(GameEntryPointAttribute).FullName!);
        private IMethodSymbol? currentMethod;

        public FrontendResult Run()
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                ValidateContainer(tree.GetCompilationUnitRoot(), model);
                foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
                {
                    if (node is CheckedStatementSyntax || node is CheckedExpressionSyntax checkedSyntax &&
                        (checkedSyntax.Keyword.IsKind(SyntaxKind.CheckedKeyword) ||
                            checkedSyntax.Expression is not CastExpressionSyntax))
                        Report("GBL007", "Only unchecked explicit numeric casts are supported as context wrappers.", node.GetLocation());
                    if (node is AttributeSyntax attribute && attribute.Parent is AttributeListSyntax list &&
                        list.Parent is not MethodDeclarationSyntax)
                        Report("GBL003", "Unsupported attribute.", node.GetLocation());
                }
            }

            if (entries.Count != 1)
                Report("GBL001", "Exactly one SDK game entry point is required.",
                    entries.Count > 1 ? entries[1].Locations[0] : compilation.SyntaxTrees.FirstOrDefault()?.GetRoot().GetLocation());
            var entry = entries.Count == 1 ? entries[0] : null;
            var graph = new Dictionary<IMethodSymbol, IReadOnlyList<IMethodSymbol>>(SymbolEqualityComparer.Default);
            if (entry is not null)
            {
                var active = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                void Visit(IMethodSymbol method)
                {
                    if (!visited.Add(method)) return;
                    active.Add(method);
                    var outgoing = calls.TryGetValue(method, out var edges) ? edges.Keys.ToArray() : [];
                    graph[method] = outgoing;
                    foreach (var callee in outgoing)
                    {
                        if (active.Contains(callee))
                            Report("GBL008", "Entry-reachable recursion is unsupported.",
                                edges![callee]);
                        else Visit(callee);
                    }
                    active.Remove(method);
                }
                Visit(entry);
            }
            return new(diagnostics.OrderBy(d => d.Path, StringComparer.Ordinal).ThenBy(d => d.Line)
                 .ThenBy(d => d.Column).ToArray(), entry, graph, compilation);
        }

        private void ValidateContainer(SyntaxNode container, SemanticModel model)
        {
            foreach (var member in container switch
            {
                CompilationUnitSyntax unit => unit.Members.Cast<MemberDeclarationSyntax>(),
                BaseNamespaceDeclarationSyntax ns => ns.Members.Cast<MemberDeclarationSyntax>(),
                ClassDeclarationSyntax cls => cls.Members.Cast<MemberDeclarationSyntax>(),
                _ => []
            })
            {
                switch (member)
                {
                    case BaseNamespaceDeclarationSyntax ns:
                        ValidateContainer(ns, model);
                        break;
                    case ClassDeclarationSyntax cls:
                        if (!cls.Modifiers.Any(SyntaxKind.StaticKeyword) || cls.TypeParameterList is not null ||
                            cls.BaseList is not null || cls.AttributeLists.Count != 0 ||
                            !AllowedModifiers(cls.Modifiers, SyntaxKind.StaticKeyword, SyntaxKind.PublicKeyword,
                                SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword))
                            Report("GBL003", "Only plain static classes are supported.", cls.Identifier.GetLocation());
                        ValidateContainer(cls, model);
                        break;
                    case MethodDeclarationSyntax method:
                        ValidateMethod(method, model);
                        break;
                    case FieldDeclarationSyntax field:
                        ValidateField(field, model);
                        break;
                    default:
                        Report("GBL003", "Unsupported declaration.", member.GetLocation());
                        break;
                }
            }
            if (container is CompilationUnitSyntax root)
                foreach (var directive in root.Usings)
                    ValidateUsing(directive);
            if (container is BaseNamespaceDeclarationSyntax namespaced)
                foreach (var directive in namespaced.Usings)
                    ValidateUsing(directive);
        }

        private void ValidateUsing(UsingDirectiveSyntax directive)
        {
            if (directive.StaticKeyword != default || directive.Alias is not null || directive.GlobalKeyword != default)
                Report("GBL003", "Only ordinary namespace using directives are supported.", directive.GetLocation());
        }

        private void ValidateMethod(MethodDeclarationSyntax method, SemanticModel model)
        {
            var symbol = model.GetDeclaredSymbol(method)!;
            var marked = symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, EntryAttribute)
                && SymbolEqualityComparer.Default.Equals(a.AttributeClass?.ContainingAssembly, sdkAssembly));
            if (marked) entries.Add(symbol);
            var validSignature = symbol.IsStatic && !symbol.IsGenericMethod && !symbol.ReturnsByRef &&
                !symbol.ReturnsByRefReadonly && symbol.Parameters.All(p =>
                p.RefKind == RefKind.None && !p.IsOptional && !p.IsParams && ValidType(p.Type)) &&
                (symbol.ReturnsVoid || ValidType(symbol.ReturnType));
            // Check every submitted method, not just those reached from the entry. In particular,
            // a declaration-only extern or partial method cannot silently enter the call graph.
            var validShape = method.Body is not null || method.ExpressionBody is not null;
            if (marked && (!validSignature || !symbol.ReturnsVoid || symbol.Parameters.Length != 0 ||
                method.Body is null || method.Parent is not ClassDeclarationSyntax cls || !cls.Modifiers.Any(SyntaxKind.StaticKeyword)))
                Report("GBL001", "Entry must be an explicit static parameterless void method with a body in a static class.", method.Identifier.GetLocation());
            if (!symbol.ReturnsVoid) CheckType(symbol.ReturnType, method.ReturnType.GetLocation());
            foreach (var parameter in method.ParameterList.Parameters)
            {
                if (parameter.Type is not null) CheckType(model.GetDeclaredSymbol(parameter)!.Type, parameter.Type.GetLocation());
                if (parameter.Default is not null || parameter.AttributeLists.Count != 0 || parameter.Modifiers.Count != 0)
                    Report("GBL003", "Unsupported parameter declaration.", parameter.GetLocation());
            }
            if (!validSignature || !symbol.IsStatic || !validShape || method.TypeParameterList is not null ||
                method.ConstraintClauses.Count != 0 || method.ExplicitInterfaceSpecifier is not null ||
                !AllowedModifiers(method.Modifiers, SyntaxKind.StaticKeyword, SyntaxKind.PublicKeyword,
                    SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword) || method.AttributeLists.SelectMany(a => a.Attributes).Count() != (marked ? 1 : 0))
                Report("GBL003", "Unsupported method declaration.", method.Identifier.GetLocation());
            calls.TryAdd(symbol, new Dictionary<IMethodSymbol, Location>(SymbolEqualityComparer.Default));
            currentMethod = symbol;
            if (method.Body is not null) CheckOperation(model.GetOperation(method.Body));
            if (method.ExpressionBody is not null) CheckOperation(model.GetOperation(method.ExpressionBody.Expression));
            currentMethod = null;
        }

        private void ValidateField(FieldDeclarationSyntax field, SemanticModel model)
        {
            var validModifiers = AllowedModifiers(field.Modifiers, SyntaxKind.StaticKeyword, SyntaxKind.ConstKeyword,
                SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword);
            if (!validModifiers || field.AttributeLists.Count != 0 ||
                (!field.Modifiers.Any(SyntaxKind.StaticKeyword) && !field.Modifiers.Any(SyntaxKind.ConstKeyword)))
                Report("GBL003", "Only static or const fields are supported.", field.GetLocation());
            foreach (var variable in field.Declaration.Variables)
            {
                var symbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (symbol is not null) CheckType(symbol.Type, field.Declaration.Type.GetLocation());
                if (variable.Initializer is null) continue;
                // A compile-time constant can still contain a forbidden operator (e.g. 2 * 3).
                // Validate the expression before checking its constant value.
                CheckOperation(model.GetOperation(variable.Initializer.Value));
                if (!model.GetConstantValue(variable.Initializer.Value).HasValue)
                    Report("GBL004", "Static field initializer must be a compile-time constant.", variable.Initializer.GetLocation());
            }
        }

        private static bool AllowedModifiers(SyntaxTokenList modifiers, params SyntaxKind[] allowed) =>
            modifiers.All(m => allowed.Contains(m.Kind())) &&
            modifiers.Count(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) ||
                m.IsKind(SyntaxKind.PrivateKeyword)) <= 1;

        private static bool ValidType(ITypeSymbol? type) => type?.SpecialType is
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_UInt16 or SpecialType.System_Int16 or SpecialType.System_Int32;

        private void CheckType(ITypeSymbol? type, Location location)
        {
            if (!ValidType(type)) Report("GBL002", "Only v0 primitive value types are supported.", location);
        }

        private void CheckOperation(IOperation? op)
        {
            if (op is null) return;
            if (op.Type is not null && op.Type.SpecialType != SpecialType.System_Void)
                CheckType(op.Type, op.Syntax.GetLocation());
            switch (op)
            {
                case IBlockOperation or IVariableDeclarationGroupOperation or IVariableDeclarationOperation or
                    IVariableInitializerOperation or IExpressionStatementOperation or
                    IReturnOperation or ILiteralOperation or IParameterReferenceOperation or IParenthesizedOperation:
                    break;
                case IBranchOperation branch:
                    if (branch.BranchKind is not (BranchKind.Break or BranchKind.Continue))
                        Report("GBL005", "Unsupported branch.", op.Syntax.GetLocation());
                    break;
                case IVariableDeclaratorOperation variable:
                    // The declarator's Type can be unsupported even with no initializer or reads.
                    CheckType(variable.Symbol.Type, op.Syntax.GetLocation());
                    if (variable.Symbol is ILocalSymbol localSymbol && localSymbol.RefKind != RefKind.None)
                        Report("GBL003", "Reference locals are unsupported.", op.Syntax.GetLocation());
                    break;
                case ILocalFunctionOperation:
                    Report("GBL003", "Local functions are unsupported.", op.Syntax.GetLocation());
                    return;
                case ILocalReferenceOperation local:
                    CheckType(local.Local.Type, op.Syntax.GetLocation());
                    break;
                case IFieldReferenceOperation field:
                    if (!field.Field.IsStatic || field.Instance is not null || !IsGameSymbol(field.Field))
                        Report("GBL003", "Only submitted static fields are supported.", op.Syntax.GetLocation());
                    CheckType(field.Field.Type, op.Syntax.GetLocation());
                    break;
                case IConditionalOperation conditional when conditional.Syntax is IfStatementSyntax:
                    break;
                case IConditionalOperation:
                    Report("GBL006", "Conditional expressions are unsupported.", op.Syntax.GetLocation());
                    return;
                case IWhileLoopOperation loop:
                    if (!loop.ConditionIsTop)
                        Report("GBL005", "Only while loops are supported.", op.Syntax.GetLocation());
                    break;
                case ISimpleAssignmentOperation assignment:
                    if (assignment.Target is not ILocalReferenceOperation and not IParameterReferenceOperation and not IFieldReferenceOperation)
                        Report("GBL006", "Unsupported assignment target.", op.Syntax.GetLocation());
                    break;
                case ICompoundAssignmentOperation compound:
                    if (compound.OperatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
                        BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr) ||
                        compound.IsChecked || compound.OperatorMethod is not null)
                        Report("GBL006", "Unsupported compound assignment.", op.Syntax.GetLocation());
                    if (compound.Target is not ILocalReferenceOperation and not IParameterReferenceOperation and not IFieldReferenceOperation)
                        Report("GBL006", "Unsupported assignment target.", op.Syntax.GetLocation());
                    break;
                case IIncrementOrDecrementOperation increment:
                    if (increment.IsChecked || increment.Target is not ILocalReferenceOperation and not IParameterReferenceOperation and not IFieldReferenceOperation ||
                        increment.OperatorMethod is not null)
                        Report("GBL006", "Unsupported increment/decrement.", op.Syntax.GetLocation());
                    break;
                case IUnaryOperation unary:
                    if (unary.OperatorKind is not (UnaryOperatorKind.Plus or UnaryOperatorKind.Minus or
                        UnaryOperatorKind.BitwiseNegation or UnaryOperatorKind.Not) || unary.IsChecked || unary.OperatorMethod is not null)
                        Report("GBL006", "Unsupported unary operator.", op.Syntax.GetLocation());
                    break;
                case IBinaryOperation binary:
                    if (binary.OperatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
                        BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr or
                        BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.LessThan or
                        BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThan or
                        BinaryOperatorKind.GreaterThanOrEqual or BinaryOperatorKind.ConditionalAnd or
                        BinaryOperatorKind.ConditionalOr) || binary.IsChecked || binary.OperatorMethod is not null)
                        Report("GBL006", "Unsupported binary operator.", op.Syntax.GetLocation());
                    break;
                case IConversionOperation conversion:
                    if (!conversion.Conversion.IsIdentity && !(conversion.Conversion.Exists &&
                        ValidType(conversion.Operand.Type) && ValidType(conversion.Type) &&
                        conversion.Operand.Type?.SpecialType != SpecialType.System_Boolean &&
                        conversion.Type?.SpecialType != SpecialType.System_Boolean &&
                        !conversion.Conversion.IsUserDefined && conversion.OperatorMethod is null && !conversion.IsChecked))
                        Report("GBL007", "Unsupported conversion.", op.Syntax.GetLocation());
                    if (conversion.IsChecked)
                        Report("GBL007", "Checked conversion is unsupported.", op.Syntax.GetLocation());
                    break;
                case IInvocationOperation invocation:
                    var target = invocation.TargetMethod;
                    if (DisplayIntrinsic.Identify(target, sdkAssembly) is DisplayIntrinsicKind.CopyToBuffer or DisplayIntrinsicKind.ReadBufferByte)
                    {
                        foreach (var argument in invocation.Arguments)
                            if (!argument.Value.ConstantValue.HasValue || argument.Value.ConstantValue.Value is not (byte or ushort))
                                Report("GBL009", "Bank copy arguments must be compile-time byte/ushort constants.", argument.Syntax.GetLocation());
                    }
                    var sprite = DisplayIntrinsic.Identify(target, sdkAssembly);
                    if (sprite is DisplayIntrinsicKind.SetSprite or DisplayIntrinsicKind.HideSprite)
                        foreach (var argument in invocation.Arguments.Where(a => a.Parameter?.Ordinal == 0 ||
                            (sprite == DisplayIntrinsicKind.SetSprite && a.Parameter?.Ordinal == 3)))
                        {
                            var bits = argument.Value.ConstantValue.Value;
                            if (!argument.Value.ConstantValue.HasValue || bits is not byte value ||
                                (argument.Parameter?.Ordinal == 0 ? value >= 40 : (value & 0x0F) != 0))
                                Report("GBL010", argument.Parameter?.Ordinal == 0
                                    ? "Sprite index must be a compile-time byte constant from 0 to 39."
                                    : "Sprite attributes must be a compile-time byte constant with bits 3..0 clear.",
                                    argument.Syntax.GetLocation());
                        }
                    if (!target.IsStatic || target.IsGenericMethod || invocation.Instance is not null ||
                        (!IsGameSymbol(target) && !IsApprovedIntrinsic(target)))
                        Report("GBL008", "Only direct game calls and approved SDK intrinsics are supported.", op.Syntax.GetLocation());
                    else if (IsGameSymbol(target) && currentMethod is not null)
                        calls[currentMethod].TryAdd(target, op.Syntax.GetLocation());
                    break;
                case IArgumentOperation:
                    break;
                default:
                    Report(op is ILoopOperation or IConditionalOperation or IThrowOperation or ITryOperation or
                        IUsingOperation or ISwitchOperation ? "GBL005" : "GBL006",
                        "Unsupported statement or expression.", op.Syntax.GetLocation());
                    return;
            }
            foreach (var child in op.ChildOperations) CheckOperation(child);
        }

        private bool IsGameSymbol(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Length != 0 &&
            symbol.DeclaringSyntaxReferences.All(r => compilation.SyntaxTrees.Contains(r.SyntaxTree)) &&
            symbol.ContainingType?.TypeKind == TypeKind.Class && symbol.ContainingType.IsStatic;

        private bool IsApprovedIntrinsic(IMethodSymbol method) => DisplayIntrinsic.Identify(method, sdkAssembly) != DisplayIntrinsicKind.None;

        private void Report(string id, string message, Location? location)
        {
            var span = location?.GetLineSpan();
            diagnostics.Add(new(id, message, span?.Path ?? "", (span?.StartLinePosition.Line ?? 0) + 1,
                (span?.StartLinePosition.Character ?? 0) + 1));
        }
    }
}
