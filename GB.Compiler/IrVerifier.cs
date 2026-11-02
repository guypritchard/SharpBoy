namespace GB.Compiler;

/// <summary>Structural, identity and type verifier. Every emitted block is entry-reachable;
/// operands and storage/call references must be the very objects defined by this module,
/// not field-equal record copies (backend static allocation is keyed by reference).</summary>
public static class IrVerifier
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new IrVerificationException(message);
    }

    public static void Verify(IrModule module)
    {
        Require(module.Functions.Count != 0, "module has no functions");
        Require(module.Functions.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() == module.Functions.Count,
            "duplicate function name");
        Require(module.Functions.Count(f => f.Name == module.EntryPoint) == 1, "missing or duplicate entry function");
        Require(module.Globals.Select(g => g.Name).Distinct(StringComparer.Ordinal).Count() == module.Globals.Count,
            "duplicate global name");
        Require(module.Constants.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() == module.Constants.Count &&
            !module.Constants.Any(c => module.Globals.Any(g => g.Name == c.Name)), "duplicate constant/storage name");
        foreach (var global in module.Globals)
            Require(ValidBits(global.Type, global.InitialValue), $"invalid global initializer {global.Name}");
        foreach (var constant in module.Constants)
            Require(ValidBits(constant.Type, constant.Value), $"invalid constant initializer {constant.Name}");
        foreach (var function in module.Functions)
        {
            Require(function.Blocks.Count > 0, $"{function.Name}: missing entry block");
            var names = function.Blocks.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
            Require(names.Count == function.Blocks.Count, $"{function.Name}: duplicate block target");
            var slots = function.Parameters.Concat(function.Locals).ToArray();
            Require(slots.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() == slots.Length,
                $"{function.Name}: duplicate slot");
            Require(function.Parameters.All(s => s.Kind == IrSlotKind.Parameter) &&
                function.Locals.All(s => s.Kind is IrSlotKind.Local or IrSlotKind.Temporary) &&
                slots.All(s => s.Type != IrType.Void), $"{function.Name}: invalid slot");
            var values = new Dictionary<string, (IrValue Value, IrBlock Block, int Index)>(StringComparer.Ordinal);
            foreach (var block in function.Blocks)
            {
                Require(block.Terminator is not null, $"{function.Name}/{block.Name}: unterminated block");
                for (var index = 0; index < block.Instructions.Count; index++)
                {
                    var instruction = block.Instructions[index];
                    void Slot(IrSlot? slot, IrGlobal? global, IrType type)
                    {
                        Require((slot is null) != (global is null) &&
                            (slot is null || slots.Any(candidate => ReferenceEquals(candidate, slot))) &&
                            (global is null || module.Globals.Any(candidate => ReferenceEquals(candidate, global))) &&
                            (slot?.Type ?? global?.Type) == type,
                            $"{function.Name}/{block.Name}: invalid storage or type");
                    }
                    switch (instruction)
                    {
                        case IrLiteral literal:
                            Require(ValidBits(literal.Value.Type, literal.Bits), "literal exceeds its type width");
                            break;
                        case IrLoad load: Slot(load.Slot, load.Global, load.Value.Type); break;
                        case IrStore store: Slot(store.Slot, store.Global, store.Value.Type); break;
                        case IrConvert convert:
                            var from = Width(convert.Operand.Type);
                            var to = Width(convert.Value.Type);
                            Require(from > 0 && to > 0 && convert.Operand.Type != IrType.Bool && convert.Value.Type != IrType.Bool &&
                                convert.Kind == (from > to ? IrConversionKind.Truncate : from < to ?
                                    Signed(convert.Operand.Type) ? IrConversionKind.SignExtend : IrConversionKind.ZeroExtend :
                                    IrConversionKind.Reinterpret), $"{function.Name}: invalid conversion width/sign");
                            break;
                        case IrUnary unary:
                            Require(unary.Value.Type == unary.Operand.Type &&
                                (unary.Kind == IrUnaryKind.LogicalNot ? unary.Value.Type == IrType.Bool :
                                 unary.Value.Type != IrType.Bool && unary.Value.Type != IrType.Void), "invalid unary type");
                            break;
                        case IrBinary binary:
                            Require(binary.Value.Type == binary.Left.Type && binary.Left.Type == binary.Right.Type &&
                                binary.Value.Type != IrType.Void &&
                                (binary.Value.Type != IrType.Bool || binary.Kind is IrBinaryKind.And or IrBinaryKind.Or or IrBinaryKind.Xor),
                                "invalid binary types");
                            break;
                        case IrCompare compare:
                            Require(compare.Value.Type == IrType.Bool && compare.Left.Type == compare.Right.Type &&
                                compare.Left.Type != IrType.Void && compare.Signed == Signed(compare.Left.Type) &&
                                (compare.Left.Type != IrType.Bool || compare.Kind is IrCompareKind.Equal or IrCompareKind.NotEqual),
                                "invalid comparison types/signedness");
                            break;
                        case IrCall call:
                            Require(module.Functions.Any(candidate => ReferenceEquals(candidate, call.Target)) &&
                                call.Arguments.Count == call.Target.Parameters.Count &&
                                call.Arguments.Zip(call.Target.Parameters).All(pair => pair.First.Type == pair.Second.Type) &&
                                (call.Target.ReturnType == IrType.Void ? call.Value is null : call.Value?.Type == call.Target.ReturnType),
                                "invalid call target/signature/result");
                            break;
                        case IrReadByte read:
                            Require(read.Address.Type == IrType.U16 && read.Value.Type == IrType.U8, "invalid ReadByte intrinsic");
                            break;
                        case IrText text:
                            Require(text.Address.Type == IrType.U16 && text.Text is not null && text.Text.Length <= 32 &&
                                text.Text.All(c => c >= 32 && c <= 126), "invalid text literal");
                            break;
                        case IrWriteByte write:
                            Require(write.Address.Type == IrType.U16 && write.Value.Type == IrType.U8 &&
                                write.Effect == IrEffect.HardwareWrite, "invalid WriteByte intrinsic");
                            break;
                        case IrDisplay display:
                            Require(Enum.IsDefined(display.Kind) && display.Effect == IrEffect.HardwareReadWrite &&
                                (display.Kind == IrDisplayKind.SetScroll ? display.Arguments.Count == 2 &&
                                    display.Arguments.All(a => a.Type == IrType.U8) : display.Arguments.Count == 0),
                                "invalid display intrinsic");
                            break;
                        case IrSprite sprite:
                            Require(Enum.IsDefined(sprite.Kind) && sprite.Coordinates is not null &&
                                sprite.Effect == IrEffect.HardwareReadWrite &&
                                sprite.Index < 40 && (sprite.Attributes & 15) == 0 &&
                                (sprite.Kind switch
                                {
                                    IrSpriteKind.Set => sprite.Coordinates.Count == 2 &&
                                        sprite.Coordinates.All(v => v is not null && v.Type == IrType.U8),
                                    IrSpriteKind.Hide => sprite.Coordinates.Count == 0 && sprite.Attributes == 0,
                                    IrSpriteKind.Initialize => sprite.Coordinates.Count == 0 && sprite.Index == 0 && sprite.Attributes == 0,
                                    _ => false
                                }),
                                "invalid sprite intrinsic");
                            break;
                        case IrBankCopy copy:
                            Require(copy.Effect == IrEffect.HardwareReadWrite && copy.Length > 0 &&
                                copy.SourceAddress is >= 0x4000 and < 0x8000 &&
                                (int)copy.SourceAddress + copy.Length <= 0x8000 &&
                                (int)copy.BufferOffset + copy.Length <= 0x800,
                                "invalid bank copy range");
                            break;
                        case IrBankRead read:
                            Require(read.Effect == IrEffect.HardwareReadWrite && read.Value.Type == IrType.U8 &&
                                read.BufferOffset < 0x800, "invalid bank buffer read");
                            break;
                        default: throw new IrVerificationException($"unknown instruction {instruction.GetType().Name}");
                    }
                    if (instruction.Result is { } result)
                        Require(values.TryAdd(result.Name, (result, block, index)) && result.Type != IrType.Void,
                            $"{function.Name}: duplicate or void value {result.Name}");
                }
                switch (block.Terminator)
                {
                    case IrBranch branch: Require(names.Contains(branch.Target), $"missing branch target {branch.Target}"); break;
                    case IrConditionalBranch branch:
                        Require(branch.Condition.Type == IrType.Bool && branch.TrueTarget != branch.FalseTarget && names.Contains(branch.TrueTarget) &&
                            names.Contains(branch.FalseTarget), $"invalid conditional branch target/condition in {block.Name}");
                        break;
                    case IrReturn ret:
                        Require(function.ReturnType == IrType.Void ? ret.Value is null : ret.Value?.Type == function.ReturnType,
                            $"invalid return in {function.Name}/{block.Name}");
                        break;
                    case IrUnreachable: break;
                    default: throw new IrVerificationException($"invalid terminator in {block.Name}");
                }
            }
            var byName = function.Blocks.ToDictionary(b => b.Name, StringComparer.Ordinal);
            var predecessors = function.Blocks.ToDictionary(b => b, _ => new HashSet<IrBlock>());
            foreach (var block in function.Blocks)
            {
                IEnumerable<string> successors = block.Terminator switch
                {
                    IrBranch branch => [branch.Target],
                    IrConditionalBranch branch => [branch.TrueTarget, branch.FalseTarget],
                    _ => []
                };
                foreach (var successor in successors) predecessors[byName[successor]].Add(block);
            }
            var entry = function.Blocks[0];
            var reachable = new HashSet<IrBlock> { entry };
            var pending = new Queue<IrBlock>();
            pending.Enqueue(entry);
            while (pending.TryDequeue(out var block))
                foreach (var successor in function.Blocks.Where(b => predecessors[b].Contains(block)))
                    if (reachable.Add(successor)) pending.Enqueue(successor);
            Require(reachable.Count == function.Blocks.Count,
                $"{function.Name}: function contains an entry-unreachable block");
            foreach (var block in reachable)
                Require(block.Terminator is not IrUnreachable, $"{function.Name}/{block.Name}: reachable unreachable terminator");

            var dominators = reachable.ToDictionary(b => b,
                b => b == entry ? new HashSet<IrBlock> { entry } : new HashSet<IrBlock>(reachable));
            bool changed;
            do
            {
                changed = false;
                foreach (var block in reachable.Where(b => b != entry))
                {
                    var pred = predecessors[block].Where(reachable.Contains).ToArray();
                    var next = new HashSet<IrBlock>(dominators[pred[0]]);
                    foreach (var previous in pred.Skip(1)) next.IntersectWith(dominators[previous]);
                    next.Add(block);
                    if (dominators[block].SetEquals(next)) continue;
                    dominators[block] = next;
                    changed = true;
                }
            } while (changed);

            void Use(IrValue operand, IrBlock block, int index)
            {
                Require(values.TryGetValue(operand.Name, out var definition) && ReferenceEquals(definition.Value, operand),
                    $"{function.Name}: undefined/noncanonical value {operand.Name}");
                Require(definition.Block == block ? definition.Index < index :
                    reachable.Contains(block) && reachable.Contains(definition.Block) &&
                    dominators[block].Contains(definition.Block),
                    $"{function.Name}/{block.Name}: value {operand.Name} does not dominate use");
            }
            foreach (var block in function.Blocks)
            {
                for (var index = 0; index < block.Instructions.Count; index++)
                {
                    var instruction = block.Instructions[index];
                    IEnumerable<IrValue> operands = instruction switch
                    {
                        IrStore s => [s.Value], IrConvert c => [c.Operand], IrUnary u => [u.Operand],
                        IrBinary b => [b.Left, b.Right], IrCompare c => [c.Left, c.Right],
                        IrReadByte r => [r.Address], IrText t => [t.Address],
                        IrCall c => c.Arguments, IrWriteByte w => [w.Address, w.Value], IrDisplay d => d.Arguments,
                        IrSprite s => s.Coordinates, _ => []
                    };
                    foreach (var operand in operands) Use(operand, block, index);
                }
                var terminalValue = block.Terminator switch
                {
                    IrConditionalBranch b => b.Condition, IrReturn r => r.Value, _ => null
                };
                if (terminalValue is not null) Use(terminalValue, block, block.Instructions.Count);
            }
        }
        // EntryPoint is the explicit stable identity of the entry function, not a
        // guessed function name. Recursion is incompatible with static per-function storage.
        var active = new HashSet<IrFunction>();
        var visited = new HashSet<IrFunction>();
        void Visit(IrFunction function)
        {
            active.Add(function);
            if (visited.Add(function))
                foreach (var call in function.Blocks.SelectMany(b => b.Instructions).OfType<IrCall>())
                {
                    if (active.Contains(call.Target))
                        throw new IrVerificationException($"entry-reachable recursive call to {call.Target.Name}", call.Source);
                    Visit(call.Target);
                }
            active.Remove(function);
        }
        Visit(module.Functions.Single(f => f.Name == module.EntryPoint));
    }

    // Signed narrow values are stored as width-masked bit patterns, not sign-extended ints.
    private static bool ValidBits(IrType type, int bits) => type switch
    {
        IrType.Bool => bits is 0 or 1,
        IrType.U8 or IrType.I8 => (bits & ~0xff) == 0,
        IrType.U16 or IrType.I16 => (bits & ~0xffff) == 0,
        IrType.I32 => true,
        _ => false
    };

    public static int Width(IrType type) => type switch
    {
        IrType.Bool or IrType.U8 or IrType.I8 => 8,
        IrType.U16 or IrType.I16 => 16, IrType.I32 => 32, _ => 0
    };
    public static bool Signed(IrType type) => type is IrType.I8 or IrType.I16 or IrType.I32;
}
