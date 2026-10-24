using System.Text;

namespace GB.Compiler;

/// <summary>DMG WRAM (fixed WRAM0 plus DMG bank-one WRAMX) and ROM0 budgets. Startup/header code is not generated here.</summary>
public sealed record Sm83Limits(int WramStart = 0xC000, int WramBytes = 0x2000,
    int StackBytes = 128, int CodeBytes = 0x3F00);

public sealed record BackendDiagnostic(string Id, IrLocation? Source, string Resource,
    int RequiredBytes, int AvailableBytes, string Contributor)
{
    public override string ToString() => $"{Id}: {Resource} requires {RequiredBytes} bytes, available {AvailableBytes} bytes ({Contributor})";
}

public sealed record MemoryAllocation(string Name, int Address, int Bytes, int? InitialBits = null);

public sealed class MemoryLayout(
    IReadOnlyList<MemoryAllocation> allocations, int wramStart, int wramBytes,
    int stackBottom, int stackTop, int stackRequired, int stackReserved, int codeBytes, bool usesDisplay = false,
    bool usesSprites = false)
{
    public IReadOnlyList<MemoryAllocation> Allocations { get; } = allocations;
    public int WramStart { get; } = wramStart;
    public int WramBytes { get; } = wramBytes;
    public int StackBottom { get; } = stackBottom;
    public int StackTop { get; } = stackTop;
    public int StackRequired { get; } = stackRequired;
    public int StackReserved { get; } = stackReserved;
    public int StackHeadroom => StackReserved - StackRequired;
    public int CodeBytes { get; } = codeBytes;
    public bool UsesDisplay { get; } = usesDisplay;
    public bool UsesSprites { get; } = usesSprites;
    public bool UsesBanking { get; init; }
    public bool UsesBackground { get; init; }
    public bool UsesSpriteInit { get; init; }

    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"WRAM ${WramStart:X4}-${WramStart + WramBytes - 1:X4} ({WramBytes} bytes)");
        foreach (var allocation in Allocations)
            text.AppendLine($"${allocation.Address:X4} {allocation.Bytes,4} {allocation.Name}" +
                (allocation.InitialBits is int bits ? " initial bits $" +
                    ((uint)bits & (allocation.Bytes == 4 ? uint.MaxValue : (1u << allocation.Bytes * 8) - 1))
                    .ToString("X" + allocation.Bytes * 2, System.Globalization.CultureInfo.InvariantCulture) : ""));
        text.AppendLine($"CPU stack ${StackBottom:X4}-${StackTop - 1:X4} (top ${StackTop:X4}, reserved {StackReserved}, required {StackRequired}, headroom {StackHeadroom})");
        text.AppendLine($"ROM0 generated code {CodeBytes} bytes (header/startup/linking not included)");
        if (UsesDisplay) text.AppendLine("Native LCD helpers in ROM0; bank 1 background requires build-supplied 64-byte tiles and 1024-byte map only when initialized");
        if (UsesSprites) text.AppendLine("Native OBJ helpers in ROM0; bank 1 tile requires build-supplied 16 bytes only when initialized; no DMA");
        if (UsesBanking) text.AppendLine("MBC1 ROMX asset staging $D000-$D7FF (2048 bytes); mapper state $C000; copy helper in ROM0");
        return text.ToString();
    }
}

public sealed record BackendResult(MemoryLayout? Layout, string? Assembly, string? MemoryReport,
    IReadOnlyList<BackendDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.Count == 0;
}

/// <summary>Static-slot, spill-after-every-IR-instruction RGBDS 0.9 SM83 code generation.</summary>
public static class Sm83Backend
{
    public static BackendResult Emit(IrModule module, Sm83Limits? limits = null, CartridgeProfile? profile = null)
    {
        limits ??= new Sm83Limits();
        profile ??= CartridgeProfile.RomOnly;
        BackendResult Fail(string id, IrLocation? source, string resource, int required, int available, string contributor) =>
            new(null, null, null, [new BackendDiagnostic(id, source, resource, required, available, contributor)]);

        if (module is null) return Fail("GBB001", null, "verified IR", 1, 0, "null module");
        try { IrVerifier.Verify(module); }
        catch (IrVerificationException error)
        { return Fail("GBB001", error.Location ?? module.Functions.FirstOrDefault()?.Source, "verified IR", 1, 0, error.Message); }

        if (limits.WramStart < 0xC000 || limits.WramBytes < 0 ||
            (long)limits.WramStart + limits.WramBytes > 0xE000 ||
            limits.StackBytes < 0 || limits.CodeBytes < 0 || limits.CodeBytes > 0x4000)
            return Fail("GBB002", module.Functions.Single(f => f.Name == module.EntryPoint).Source,
                "DMG region limits", Math.Max(0, limits.WramBytes),
                limits.WramStart is >= 0xC000 and <= 0xE000 ? 0xE000 - limits.WramStart : 0,
                "invalid WRAM0/WRAMX/ROM0 range or negative capacity");

        var entry = module.Functions.Single(f => f.Name == module.EntryPoint);
        foreach (var copy in module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IrBankCopy>())
            if (!profile.ContainsCopy(copy))
                return Fail("GBB007", copy.Source, "declared MBC1 asset span", copy.Length, 0,
                    $"bank {copy.Bank} source ${copy.SourceAddress:X4} length {copy.Length} offset ${copy.BufferOffset:X4}");
        foreach (var read in module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IrBankRead>())
            if (!profile.IsMbc1)
                return Fail("GBB007", read.Source, "MBC1 staging buffer", 1, 0, "ROM-only profile");
        var reachable = new HashSet<IrFunction>();
        var active = new HashSet<IrFunction>();
        int Depth(IrFunction function)
        {
            if (!active.Add(function)) throw new IrVerificationException($"recursive call to {function.Name}");
            reachable.Add(function);
            int depth = 0;
            foreach (var instruction in function.Blocks.SelectMany(b => b.Instructions))
            {
                if (instruction is IrCall call) depth = Math.Max(depth, checked(2 + Depth(call.Target)));
                if (instruction is IrDisplay { Kind: IrDisplayKind.InitializeBackground or IrDisplayKind.WaitForNextVBlank })
                    depth = Math.Max(depth, 2); // native helper CALL, no nested calls/pushes
                if (instruction is IrBankCopy) depth = Math.Max(depth, 4); // CALL + helper PUSH AF
                if (instruction is IrSprite sprite) depth = Math.Max(depth,
                    sprite.Kind == IrSpriteKind.Initialize ? 4 : 2); // init PUSH AF; Set/Hide CALL wait helper
            }
            active.Remove(function);
            return depth;
        }
        int stackRequired;
        try { stackRequired = checked(2 + Depth(entry)); } // startup CALL entry; no generated PUSH or helpers
        catch (Exception error) when (error is IrVerificationException or OverflowException)
        { return Fail("GBB001", entry.Source, "call graph", 0, 0, error.Message); }
        foreach (var global in module.Globals)
            if (IrVerifier.Width(global.Type) == 0)
                return Fail("GBB005", global.Source, "SM83 type", 0, 0, global.Name);
        foreach (var function in module.Functions.Where(reachable.Contains))
        {
            if (function.ReturnType != IrType.Void && IrVerifier.Width(function.ReturnType) == 0)
                return Fail("GBB005", function.Source, "SM83 return type", 0, 0, function.Name);
            foreach (var slot in function.Parameters.Concat(function.Locals))
                if (IrVerifier.Width(slot.Type) == 0)
                    return Fail("GBB005", slot.Source, "SM83 slot type", 0, 0, $"{function.Name}/{slot.Name}");
            foreach (var instruction in function.Blocks.SelectMany(b => b.Instructions))
            {
                if (instruction.Result is { } value && IrVerifier.Width(value.Type) == 0)
                    return Fail("GBB005", instruction.Source, "SM83 value type", 0, 0, $"{function.Name}/{value.Name}");
                if (instruction is IrCompare compare && !Enum.IsDefined(compare.Kind))
                    return Fail("GBB005", instruction.Source, "SM83 comparison", 0, 0, $"{function.Name}/{compare.Kind}");
                if (instruction is IrBinary binary && !Enum.IsDefined(binary.Kind))
                    return Fail("GBB005", instruction.Source, "SM83 binary", 0, 0, $"{function.Name}/{binary.Kind}");
                if (instruction is IrUnary unary && !Enum.IsDefined(unary.Kind))
                    return Fail("GBB005", instruction.Source, "SM83 unary", 0, 0, $"{function.Name}/{unary.Kind}");
            }
        }
        if (stackRequired > limits.StackBytes)
            return Fail("GBB004", entry.Source, "CPU stack", stackRequired, limits.StackBytes, entry.Name);
        if (limits.StackBytes > limits.WramBytes)
            return Fail("GBB003", entry.Source, "WRAM0", limits.StackBytes, limits.WramBytes, "CPU stack reserve");

        var allocations = new List<MemoryAllocation>();
        var addresses = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        // A literal WRAM address can be passed through conversions/copies before a
        // hardware write. Conservatively keep every literal WRAM byte outside static
        // spill storage so result buffers cannot alias later function temporaries.
            // Evaluate only immutable IR definitions. A load (even from a constant-initialized
            // slot) is not proof: calls, branches and stores may change that storage.
            var definitions = new Dictionary<IrValue, IrInstruction>(ReferenceEqualityComparer.Instance);
            foreach (var instruction in module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions))
                if (instruction.Result is { } result) definitions.Add(result, instruction);
            var evaluating = new HashSet<IrValue>(ReferenceEqualityComparer.Instance);
            int? Constant(IrValue value)
            {
                if (!definitions.TryGetValue(value, out var definition) || !evaluating.Add(value)) return null;
                try
                {
                    int? bits = definition switch
                    {
                        IrLiteral literal => literal.Bits,
                        IrConvert convert => Constant(convert.Operand) is int operand ?
                            convert.Kind == IrConversionKind.SignExtend
                                ? convert.Operand.Type switch
                                {
                                    IrType.I8 => (sbyte)operand,
                                    IrType.I16 => (short)operand,
                                    _ => operand
                                }
                                : operand : null,
                        IrBinary binary when Constant(binary.Left) is int left && Constant(binary.Right) is int right =>
                            binary.Kind switch
                            {
                                IrBinaryKind.Add => unchecked(left + right),
                                IrBinaryKind.Subtract => unchecked(left - right),
                                IrBinaryKind.And => left & right,
                                IrBinaryKind.Or => left | right,
                                IrBinaryKind.Xor => left ^ right,
                                _ => null
                            },
                        IrUnary unary when Constant(unary.Operand) is int operand => unary.Kind switch
                        {
                            IrUnaryKind.Plus => operand,
                            IrUnaryKind.Negate => unchecked(-operand),
                            IrUnaryKind.BitwiseNot => ~operand,
                            IrUnaryKind.LogicalNot => operand == 0 ? 1 : 0,
                            _ => null
                        },
                        _ => null
                    };
                    return bits is int n ? IrVerifier.Width(value.Type) switch
                    {
                        8 => n & 0xFF,
                        16 => n & 0xFFFF,
                        _ => n
                    } : null;
                }
                finally { evaluating.Remove(value); }
            }
        var reservedWrites = module.Functions.Where(reachable.Contains)
            .SelectMany(f => f.Blocks).SelectMany(b => b.Instructions)
            .OfType<IrLiteral>().Select(l => l.Bits)
            .Where(address => address is >= 0xC000 and < 0xE000).ToHashSet();
        if (profile.IsMbc1) { reservedWrites.Add(0xC000); reservedWrites.Add(0xC001); } // mapper state
        foreach (var write in module.Functions.Where(reachable.Contains).SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IrWriteByte>())
            if (Constant(write.Address) is int address && address is >= 0xC000 and < 0xE000)
                reservedWrites.Add(address);
        var cursor = limits.WramStart;
        var stackBottom = limits.WramStart + limits.WramBytes - limits.StackBytes;
        if (profile.IsMbc1 && stackBottom < 0xD800 && stackBottom < limits.WramStart + limits.WramBytes &&
            limits.WramStart + limits.WramBytes > 0xD000)
            return Fail("GBB003", entry.Source, "staging/CPU stack separation", 0xD800, stackBottom, "stack intersects $D000-$D7FF");
        BackendDiagnostic? overflow = null;
        void Allocate(object key, string name, IrType type, IrLocation source, int? initialBits = null)
        {
            if (overflow is not null) return;
            if (addresses.ContainsKey(key))
            {
                overflow = new BackendDiagnostic("GBB005", source, "SM83 static slots", 0, 0,
                    $"storage/value object reused in multiple positions: {name}");
                return;
            }
            var bytes = IrVerifier.Width(type) / 8;
            // A primitive must stay wholly within one physical DMG WRAM bank.
            while (true)
            {
                if (cursor < 0xD000 && cursor + bytes > 0xD000) { cursor = 0xD000; continue; }
                if (profile.IsMbc1 && cursor < 0xD800 && cursor + bytes > 0xD000) { cursor = 0xD800; continue; }
                var collision = Enumerable.Range(cursor, bytes).FirstOrDefault(reservedWrites.Contains, -1);
                if (collision < 0) break;
                cursor = collision + 1;
            }
            var required = cursor - limits.WramStart + bytes;
            var available = stackBottom - limits.WramStart;
            if (required > available)
            {
                overflow = new BackendDiagnostic("GBB003", source, "WRAM0 static slots", required, available, name);
                return;
            }
            addresses.Add(key, cursor);
            allocations.Add(new MemoryAllocation(name, cursor, bytes, initialBits));
            cursor += bytes;
        }
        foreach (var global in module.Globals.OrderBy(g => g.Name, StringComparer.Ordinal))
            Allocate(global, $"global {global.Name}", global.Type, global.Source, global.InitialValue);
        foreach (var (function, index) in module.Functions.Select((f, i) => (f, i)).Where(pair => reachable.Contains(pair.f)))
        {
            foreach (var parameter in function.Parameters)
                Allocate(parameter, $"fn{index} {function.Name} param {parameter.Name}", parameter.Type, parameter.Source);
            foreach (var local in function.Locals)
                Allocate(local, $"fn{index} {function.Name} {local.Kind.ToString().ToLowerInvariant()} {local.Name}", local.Type, local.Source);
            foreach (var value in function.Blocks.SelectMany(b => b.Instructions).Select(i => i.Result).OfType<IrValue>())
                Allocate(value, $"fn{index} {function.Name} value {value.Name}", value.Type, function.Source);
            if (function.ReturnType != IrType.Void)
                Allocate(function, $"fn{index} {function.Name} return", function.ReturnType, function.Source);
        }
        if (overflow is not null) return new(null, null, null, [overflow]);

        if (profile.IsMbc1)
        {
            foreach (var write in module.Functions.SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).OfType<IrWriteByte>())
            {
                var known = Constant(write.Address);
                if (known is not int address)
                    return Fail("GBB007", write.Source, "MBC1 raw write address", 1, 0,
                        "GameMemory.WriteByte address must be statically provable; use a literal safe address");
                // E000-FDFF echoes C000-DDFF on DMG, including mapper state and staged bytes.
                var physical = address is >= 0xE000 and < 0xFE00 ? address - 0x2000 : address;
                if (address < 0x8000 || physical is >= 0xC000 and <= 0xC001 ||
                    physical is >= 0xD000 and < 0xD800 ||
                    physical >= stackBottom && physical < limits.WramStart + limits.WramBytes ||
                    allocations.Any(a => physical >= a.Address && physical < a.Address + a.Bytes))
                    return Fail("GBB007", write.Source, "MBC1 raw write address", 1, 0,
                        $"${address:X4} is mapper-owned or reserved runtime memory; choose unallocated WRAM");
            }
        }

        var writer = new Writer(module, reachable, addresses, allocations, limits, stackBottom);
        try { writer.Generate(); }
        catch (NotSupportedException error) { return Fail("GBB005", writer.CurrentSource ?? entry.Source, "SM83 instruction", 0, 0, error.Message); }
        catch (KeyNotFoundException error) { return Fail("GBB001", writer.CurrentSource ?? entry.Source, "verified IR allocation", 1, 0, error.Message); }
        if (writer.CodeBytes > limits.CodeBytes)
            return Fail("GBB006", entry.Source, "ROM0 generated code", writer.CodeBytes, limits.CodeBytes, entry.Name);
        var display = module.Functions.Where(reachable.Contains).SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrDisplay);
        var sprites = module.Functions.Where(reachable.Contains).SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrSprite);
        var background = module.Functions.Where(reachable.Contains).SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrDisplay { Kind: IrDisplayKind.InitializeBackground });
        var spriteInit = module.Functions.Where(reachable.Contains).SelectMany(f => f.Blocks).SelectMany(b => b.Instructions).Any(i => i is IrSprite { Kind: IrSpriteKind.Initialize });
        var layout = new MemoryLayout(allocations.AsReadOnly(), limits.WramStart, limits.WramBytes,
            stackBottom, limits.WramStart + limits.WramBytes, stackRequired, limits.StackBytes, writer.CodeBytes, display, sprites)
            { UsesBanking = profile.IsMbc1, UsesBackground = background, UsesSpriteInit = spriteInit };
        return new(layout, writer.Text, layout.Report(), []);
    }

    private sealed class Writer(IrModule module, HashSet<IrFunction> reachable,
        Dictionary<object, int> addresses, List<MemoryAllocation> allocations, Sm83Limits limits, int stackBottom)
    {
        private readonly StringBuilder text = new();
        private int serial;
        public int CodeBytes { get; private set; }
        public IrLocation? CurrentSource { get; private set; }
        public string Text => text.ToString();
        private string Next(string tag) => $".gen_{serial++}_{tag}";
        private static string Hex(int number) => $"${number:X4}";
        private int At(object value, int byteIndex = 0) => addresses[value] + byteIndex;
        private static int Width(IrType type) => IrVerifier.Width(type) / 8;
        private void Label(string name) => text.AppendLine(name + ":");
        private void Line(string line, int size)
        {
            text.Append("    ").AppendLine(line);
            CodeBytes += size;
        }
        private void Load(object value, int offset = 0) => Line($"ld a, [{Hex(At(value, offset))}]", 3);
        private void Store(object value, int offset = 0) => Line($"ld [{Hex(At(value, offset))}], a", 3);
        private void Jump(string label) => Line($"jp {label}", 3);
        private void Copy(object from, object to, int count)
        {
            for (int i = 0; i < count; i++) { Load(from, i); Store(to, i); }
        }

        public void Generate()
        {
            text.AppendLine("; Preserve the anchored ROM0[$0150] startup section across WRAM declarations.");
            text.AppendLine("PUSHS");
            void Section(string title, int address) => text.AppendLine(address < 0xD000
                ? $"SECTION \"{title}\", WRAM0[{Hex(address)}]"
                : $"SECTION \"{title}\", WRAMX[{Hex(address)}], BANK[1]");
            foreach (var allocation in allocations)
            {
                Section($"GB slot {allocation.Address:X4}", allocation.Address);
                Label($"gb_ram_{allocation.Address:X4}:");
                text.AppendLine($"    ds {allocation.Bytes} ; {allocation.Name}");
            }
            var stackCursor = stackBottom;
            while (stackCursor < limits.WramStart + limits.WramBytes)
            {
                var end = Math.Min(stackCursor < 0xD000 ? 0xD000 : 0xE000, limits.WramStart + limits.WramBytes);
                Section($"GB CPU stack {stackCursor:X4}", stackCursor);
                if (stackCursor == stackBottom) Label("gb_stack_bottom:");
                text.AppendLine($"    ds {end - stackCursor} ; downward CPU stack");
                stackCursor = end;
            }
            // Absolute address is usable even when the stack ends at the WRAM boundary.
            text.AppendLine($"DEF gb_stack_top EQU {Hex(limits.WramStart + limits.WramBytes)}");
            text.AppendLine("EXPORT gb_stack_top");
            text.AppendLine("POPS ; generated code continues the anchored startup ROM0 section");
            var entryIndex = module.Functions.FindIndex(f => f.Name == module.EntryPoint);
            foreach (var (function, index) in module.Functions.Select((f, i) => (f, i)).Where(pair => reachable.Contains(pair.f)))
            {
                if (index == entryIndex) Label("gb_entry:");
                Label($"gb_fn_{index}:");
                foreach (var (block, blockIndex) in function.Blocks.Select((b, i) => (b, i)))
                {
                    var prefix = $"gb_fn_{index}_b{blockIndex}";
                    Label(prefix);
                    foreach (var instruction in block.Instructions)
                    {
                        CurrentSource = instruction.Source;
                        Instruction(instruction);
                    }
                    CurrentSource = block.Terminator?.Source;
                    string Target(string name) => $"gb_fn_{index}_b{function.Blocks.FindIndex(b => b.Name == name)}";
                    switch (block.Terminator)
                    {
                        case IrBranch branch: Jump(Target(branch.Target)); break;
                        case IrConditionalBranch branch:
                            Load(branch.Condition);
                            Line("and a", 1);
                            Line($"jp nz, {Target(branch.TrueTarget)}", 3);
                            Jump(Target(branch.FalseTarget));
                            break;
                        case IrReturn ret:
                            if (ret.Value is not null) Copy(ret.Value, function, Width(function.ReturnType));
                            Line("ret", 1);
                            break;
                        default: throw new NotSupportedException($"terminator {block.Terminator?.GetType().Name}");
                    }
                }
            }
        }

        private void Instruction(IrInstruction instruction)
        {
            switch (instruction)
            {
                case IrLiteral literal:
                    for (int i = 0; i < Width(literal.Value.Type); i++)
                    {
                        Line($"ld a, ${(byte)(literal.Bits >> (i * 8)):X2}", 2);
                        Store(literal.Value, i);
                    }
                    break;
                case IrLoad load: Copy((object?)load.Slot ?? load.Global!, load.Value, Width(load.Value.Type)); break;
                case IrStore store: Copy(store.Value, (object?)store.Slot ?? store.Global!, Width(store.Value.Type)); break;
                case IrConvert convert:
                    var from = Width(convert.Operand.Type);
                    var to = Width(convert.Value.Type);
                    Copy(convert.Operand, convert.Value, Math.Min(from, to));
                    if (to > from)
                    {
                        if (convert.Kind == IrConversionKind.SignExtend)
                        {
                            Load(convert.Operand, from - 1);
                            Line("rlca", 1); // bit 7 into carry
                            Line("sbc a, a", 1); // $FF on negative, $00 otherwise
                        }
                        else Line("xor a", 1);
                        for (int i = from; i < to; i++) Store(convert.Value, i);
                    }
                    break;
                case IrUnary unary:
                    if (unary.Kind == IrUnaryKind.LogicalNot)
                    {
                        Load(unary.Operand);
                        Line("xor $01", 2);
                        Store(unary.Value);
                    }
                    else for (int i = 0; i < Width(unary.Value.Type); i++)
                    {
                        Load(unary.Operand, i);
                        switch (unary.Kind)
                        {
                            case IrUnaryKind.Plus: break;
                            case IrUnaryKind.BitwiseNot: Line("cpl", 1); break;
                            case IrUnaryKind.Negate:
                                Line("ld b, a", 1);
                                Line("ld a, $00", 2); // LD leaves previous borrow untouched
                                Line(i == 0 ? "sub b" : "sbc a, b", 1);
                                break;
                            default: throw new NotSupportedException($"unary {unary.Kind}");
                        }
                        Store(unary.Value, i);
                    }
                    break;
                case IrBinary binary:
                    for (int i = 0; i < Width(binary.Value.Type); i++)
                    {
                        Load(binary.Right, i);
                        Line("ld b, a", 1);
                        Load(binary.Left, i);
                        Line(binary.Kind switch
                        {
                            IrBinaryKind.Add => i == 0 ? "add a, b" : "adc a, b",
                            IrBinaryKind.Subtract => i == 0 ? "sub b" : "sbc a, b",
                            IrBinaryKind.And => "and b", IrBinaryKind.Or => "or b", IrBinaryKind.Xor => "xor b",
                            _ => throw new NotSupportedException($"binary {binary.Kind}")
                        }, 1);
                        Store(binary.Value, i);
                    }
                    break;
                case IrCompare compare: Compare(compare); break;
                case IrCall call:
                    for (int i = 0; i < call.Arguments.Count; i++)
                        Copy(call.Arguments[i], call.Target.Parameters[i], Width(call.Arguments[i].Type));
                    JumpCall(call.Target);
                    if (call.Value is not null) Copy(call.Target, call.Value, Width(call.Value.Type));
                    break;
                case IrWriteByte write:
                    Load(write.Address);
                    Line("ld l, a", 1);
                    Load(write.Address, 1);
                    Line("ld h, a", 1);
                    Load(write.Value);
                    Line("ld [hl], a", 1);
                    break;
                case IrBankRead read:
                    Line($"ld a, [${0xD000 + read.BufferOffset:X4}]", 3);
                    Store(read.Value);
                    break;
                case IrBankCopy copy:
                    Line($"ld a, ${copy.Bank:X2}", 2);
                    Line($"ld hl, ${copy.SourceAddress:X4}", 3);
                    Line($"ld de, ${0xD000 + copy.BufferOffset:X4}", 3);
                    Line($"ld bc, ${copy.Length:X4}", 3);
                    Line("call gb_bank_copy", 3);
                    break;
                case IrDisplay display:
                    switch (display.Kind)
                    {
                        case IrDisplayKind.InitializeBackground: Line("call gb_display_init", 3); break;
                        case IrDisplayKind.WaitForNextVBlank: Line("call gb_wait_next_vblank", 3); break;
                        case IrDisplayKind.SetScroll:
                            Load(display.Arguments[0]);
                            Line("ldh [$FF43], a", 2);
                            Load(display.Arguments[1]);
                            Line("ldh [$FF42], a", 2);
                            break;
                        default: throw new NotSupportedException($"display {display.Kind}");
                    }
                    break;
                case IrSprite sprite:
                    if (sprite.Kind == IrSpriteKind.Initialize)
                    {
                        Line("call gb_sprite_init", 3);
                        break;
                    }
                    Line("call gb_wait_oam_safe", 3);
                    Line($"ld hl, ${0xFE00 + sprite.Index * 4:X4}", 3);
                    if (sprite.Kind == IrSpriteKind.Hide)
                    {
                        Line("xor a", 1);
                        Line("ld [hl], a", 1);
                    }
                    else
                    {
                        Load(sprite.Coordinates[1]); // raw Y, raw X, fixed tile 4, attributes
                        Line("ld [hl+], a", 1);
                        Load(sprite.Coordinates[0]);
                        Line("ld [hl+], a", 1);
                        Line("ld a, $04", 2);
                        Line("ld [hl+], a", 1);
                        Line($"ld a, ${sprite.Attributes:X2}", 2);
                        Line("ld [hl], a", 1);
                    }
                    break;
                default: throw new NotSupportedException($"instruction {instruction.GetType().Name}");
            }
        }

        private void JumpCall(IrFunction function) => Line($"call gb_fn_{module.Functions.IndexOf(function)}", 3);

        private void Compare(IrCompare compare)
        {
            var equal = Next("eq");
            var different = Next("diff");
            var less = Next("less");
            var greater = Next("greater");
            var done = Next("done");
            for (int i = Width(compare.Left.Type) - 1; i >= 0; i--)
            {
                Load(compare.Right, i);
                if (compare.Signed && i == Width(compare.Left.Type) - 1) Line("xor $80", 2);
                Line("ld b, a", 1);
                Load(compare.Left, i);
                if (compare.Signed && i == Width(compare.Left.Type) - 1) Line("xor $80", 2);
                Line("cp b", 1);
                Line($"jp nz, {different}", 3);
            }
            Jump(equal);
            Label(different);
            Line($"jp c, {less}", 3);
            Jump(greater);
            void Result(string label, bool truth)
            {
                Label(label);
                Line($"ld a, ${(truth ? 1 : 0):X2}", 2);
                Jump(done);
            }
            Result(equal, compare.Kind is IrCompareKind.Equal or IrCompareKind.LessEqual or IrCompareKind.GreaterEqual);
            Result(less, compare.Kind is IrCompareKind.NotEqual or IrCompareKind.Less or IrCompareKind.LessEqual);
            Result(greater, compare.Kind is IrCompareKind.NotEqual or IrCompareKind.Greater or IrCompareKind.GreaterEqual);
            Label(done);
            Store(compare.Value);
        }
    }
}
