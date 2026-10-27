using Microsoft.CodeAnalysis;

namespace GB.Compiler;

public enum IrType { Void, Bool, U8, I8, U16, I16, I32 }
public enum IrSlotKind { Parameter, Local, Temporary }
public enum IrUnaryKind { Plus, Negate, BitwiseNot, LogicalNot }
public enum IrBinaryKind { Add, Subtract, And, Or, Xor }
public enum IrCompareKind { Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual }
public enum IrConversionKind { Truncate, SignExtend, ZeroExtend, Reinterpret }
public enum IrEffect { Pure, ReadsStorage, WritesStorage, Call, HardwareWrite, HardwareReadWrite }

public sealed record IrLocation(string Path, int Line, int Column, int Start, int Length)
{
    public static IrLocation From(Location location)
    {
        var line = location.GetLineSpan();
        return new(line.Path, line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1,
            location.SourceSpan.Start, location.SourceSpan.Length);
    }
}

// Operand/storage records are identity-bearing definitions: never clone them for a use.
// IrVerifier enforces canonical references before reference-keyed backend allocation.
public sealed record IrValue(string Name, IrType Type);
public sealed record IrSlot(string Name, IrType Type, IrSlotKind Kind, IrLocation Source);
public sealed record IrGlobal(string Name, IrType Type, int InitialValue, IrLocation Source);
public sealed record IrConstant(string Name, IrType Type, int Value, IrLocation Source);

public abstract record IrInstruction(IrLocation Source)
{
    public abstract IrEffect Effect { get; }
    public abstract IrValue? Result { get; }
}
public sealed record IrLiteral(IrValue Value, int Bits, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.Pure;
    public override IrValue Result => Value;
}
public sealed record IrLoad(IrValue Value, IrSlot? Slot, IrGlobal? Global, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.ReadsStorage;
    public override IrValue Result => Value;
}
public sealed record IrStore(IrValue Value, IrSlot? Slot, IrGlobal? Global, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.WritesStorage;
    public override IrValue? Result => null;
}
public sealed record IrConvert(IrValue Value, IrValue Operand, IrConversionKind Kind, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.Pure;
    public override IrValue Result => Value;
}
public sealed record IrUnary(IrValue Value, IrValue Operand, IrUnaryKind Kind, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.Pure;
    public override IrValue Result => Value;
}
public sealed record IrBinary(IrValue Value, IrValue Left, IrValue Right, IrBinaryKind Kind, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.Pure;
    public override IrValue Result => Value;
}
public sealed record IrCompare(IrValue Value, IrValue Left, IrValue Right, IrCompareKind Kind, bool Signed, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.Pure;
    public override IrValue Result => Value;
}
public sealed record IrCall(IrValue? Value, IrFunction Target, IReadOnlyList<IrValue> Arguments, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.Call;
    public override IrValue? Result => Value;
}
/// <summary>Resolved, SDK-verified GameMemory.WriteByte. Always observable, never a named call.</summary>
public sealed record IrWriteByte(IrValue Address, IrValue Value, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareWrite;
    public override IrValue? Result => null;
}
/// <summary>Ordered native memory read, including device side effects.</summary>
public sealed record IrReadByte(IrValue Value, IrValue Address, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareReadWrite;
    public override IrValue Result => Value;
}
/// <summary>Compile-time text expanded into tile writes.</summary>
public sealed record IrText(IrValue Address, string Text, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareWrite;
    public override IrValue? Result => null;
}
public enum IrDisplayKind { InitializeBackground, WaitForNextVBlank, SetScroll }
/// <summary>Ordered, observable LCD polling/VRAM copy or scroll-register writes.</summary>
public sealed record IrDisplay(IrDisplayKind Kind, IReadOnlyList<IrValue> Arguments, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareReadWrite;
    public override IrValue? Result => null;
}
public enum IrSpriteKind { Initialize, Set, Hide }
/// <summary>Ordered SDK-only OBJ operation; index/attributes are validated immediate operands.</summary>
public sealed record IrSprite(IrSpriteKind Kind, byte Index, byte Attributes, IReadOnlyList<IrValue> Coordinates,
    IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareReadWrite;
    public override IrValue? Result => null;
}
/// <summary>SDK-verified, ordered transfer from a declared ROMX asset into fixed WRAM staging.</summary>
public sealed record IrBankCopy(byte Bank, ushort SourceAddress, ushort BufferOffset, ushort Length, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareReadWrite;
    public override IrValue? Result => null;
}
public sealed record IrBankRead(IrValue Value, ushort BufferOffset, IrLocation Location) : IrInstruction(Location)
{
    public override IrEffect Effect => IrEffect.HardwareReadWrite;
    public override IrValue Result => Value;
}

public abstract record IrTerminator(IrLocation Source);
public sealed record IrBranch(string Target, IrLocation Location) : IrTerminator(Location);
public sealed record IrConditionalBranch(IrValue Condition, string TrueTarget, string FalseTarget, IrLocation Location) : IrTerminator(Location);
public sealed record IrReturn(IrValue? Value, IrLocation Location) : IrTerminator(Location);
/// <summary>A syntactically present block with no path from the function entry.</summary>
public sealed record IrUnreachable(IrLocation Location) : IrTerminator(Location);

public sealed class IrBlock(string name, IrLocation source)
{
    public string Name { get; } = name;
    public IrLocation Source { get; } = source;
    public List<IrInstruction> Instructions { get; } = [];
    public IrTerminator? Terminator { get; private set; }
    public void Terminate(IrTerminator terminator)
    {
        if (Terminator is not null) throw new IrVerificationException($"Block {Name} already has a terminator.");
        Terminator = terminator;
    }
}

public sealed class IrFunction(string name, IrType returnType, IrLocation source)
{
    public string Name { get; } = name;
    public IrType ReturnType { get; } = returnType;
    public IrLocation Source { get; } = source;
    public List<IrSlot> Parameters { get; } = [];
    public List<IrSlot> Locals { get; } = [];
    public List<IrBlock> Blocks { get; } = [];
}

public sealed class IrModule(string entryPoint)
{
    public string EntryPoint { get; } = entryPoint;
    public List<IrGlobal> Globals { get; } = [];
    public List<IrConstant> Constants { get; } = [];
    public List<IrFunction> Functions { get; } = [];
}

public sealed class IrVerificationException(string message, IrLocation? source = null) : InvalidOperationException($"IR verification: {message}")
{
    public IrLocation? Location { get; } = source;
}
