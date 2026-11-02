using System.Text;

namespace GB.Compiler;

/// <summary>Stable, line-oriented dump with optional source annotations; no Roslyn identities.</summary>
public static class IrFormatter
{
    public static string Format(IrModule module, bool includeSource = false)
    {
        var output = new StringBuilder();
        string Source(IrLocation location) => includeSource ?
            $" @ {location.Path}:{location.Line}:{location.Column} [{location.Start}+{location.Length}]" : "";
        static string V(IrValue value) => $"%{value.Name}:{value.Type}";
        static string Storage(IrSlot? slot, IrGlobal? global) => slot is not null ? $"${slot.Name}" : $"@{global?.Name}";
        output.AppendLine($"module entry {module.EntryPoint}");
        foreach (var global in module.Globals)
            output.AppendLine($"global @{global.Name}:{global.Type} = {global.InitialValue}{Source(global.Source)}");
        foreach (var constant in module.Constants)
            output.AppendLine($"const @{constant.Name}:{constant.Type} = {constant.Value}{Source(constant.Source)}");
        foreach (var function in module.Functions)
        {
            output.AppendLine($"function {function.Name} -> {function.ReturnType}{Source(function.Source)}");
            foreach (var slot in function.Parameters.Concat(function.Locals))
                output.AppendLine($"  {slot.Kind.ToString().ToLowerInvariant()} ${slot.Name}:{slot.Type}{Source(slot.Source)}");
            foreach (var block in function.Blocks)
            {
                output.AppendLine($"  {block.Name}:{Source(block.Source)}");
                foreach (var instruction in block.Instructions)
                {
                    var text = instruction switch
                    {
                        IrLiteral i => $"{V(i.Value)} = const {i.Bits}",
                        IrLoad i => $"{V(i.Value)} = load {Storage(i.Slot, i.Global)}",
                        IrStore i => $"store {Storage(i.Slot, i.Global)}, {V(i.Value)}",
                        IrConvert i => $"{V(i.Value)} = convert {i.Kind.ToString().ToLowerInvariant()} {V(i.Operand)}",
                        IrUnary i => $"{V(i.Value)} = {i.Kind.ToString().ToLowerInvariant()} {V(i.Operand)}",
                        IrBinary i => $"{V(i.Value)} = {i.Kind.ToString().ToLowerInvariant()} {V(i.Left)}, {V(i.Right)} unchecked",
                        IrCompare i => $"{V(i.Value)} = cmp.{i.Kind.ToString().ToLowerInvariant()}.{(i.Signed ? "signed" : "unsigned")} {V(i.Left)}, {V(i.Right)}",
                        IrCall i => $"{(i.Value is null ? "" : V(i.Value) + " = ")}call {i.Target.Name}({string.Join(", ", i.Arguments.Select(V))})",
                        IrReadByte i => $"{V(i.Value)} = intrinsic.readbyte observable {V(i.Address)}",
                        IrText i => $"intrinsic.text observable {V(i.Address)}, {System.Text.Json.JsonSerializer.Serialize(i.Text)}",
                        IrWriteByte i => $"intrinsic.writebyte observable {V(i.Address)}, {V(i.Value)}",
                        IrDisplay i => $"intrinsic.display.{i.Kind.ToString().ToLowerInvariant()} observable {string.Join(", ", i.Arguments.Select(V))}",
                        IrSprite i => $"intrinsic.sprite.{i.Kind.ToString().ToLowerInvariant()} observable index {i.Index} attributes ${i.Attributes:X2} {string.Join(", ", i.Coordinates.Select(V))}",
                        IrBankCopy i => $"intrinsic.bankcopy observable bank {i.Bank} source ${i.SourceAddress:X4} buffer +${i.BufferOffset:X4} length {i.Length}",
                        IrBankRead i => $"intrinsic.bankread observable buffer +${i.BufferOffset:X4} -> {i.Value.Name}",
                        _ => throw new IrVerificationException("unknown instruction in formatter")
                    };
                    output.AppendLine($"    {text}{Source(instruction.Source)}");
                }
                var terminator = block.Terminator switch
                {
                    IrBranch b => $"br {b.Target}",
                    IrConditionalBranch b => $"br_if {V(b.Condition)}, {b.TrueTarget}, {b.FalseTarget}",
                    IrReturn r => r.Value is null ? "ret" : $"ret {V(r.Value)}",
                    IrUnreachable => "unreachable",
                    _ => throw new IrVerificationException($"unterminated block {block.Name}")
                };
                output.AppendLine($"    {terminator}{Source(block.Terminator!.Source)}");
            }
        }
        return output.ToString();
    }
}
