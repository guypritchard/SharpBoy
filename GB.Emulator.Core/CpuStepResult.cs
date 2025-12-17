#nullable enable
using System;
using System.Collections.Generic;

namespace GB.Emulator.Core
{
    public sealed class CpuStepResult
    {
        public CpuStepResult(
            Instruction instruction,
            ushort address,
            byte operand1,
            byte operand2,
            IReadOnlyCollection<ushort>? writtenAddresses = null,
            IReadOnlyCollection<ushort>? readAddresses = null)
        {
            Instruction = instruction;
            Address = address;
            Operand1 = operand1;
            Operand2 = operand2;
            WrittenAddresses = writtenAddresses ?? Array.Empty<ushort>();
            ReadAddresses = readAddresses ?? Array.Empty<ushort>();
        }

        public Instruction Instruction { get; }

        public ushort Address { get; }

        public byte Operand1 { get; }

        public byte Operand2 { get; }

        public IReadOnlyCollection<ushort> WrittenAddresses { get; }

        public IReadOnlyCollection<ushort> ReadAddresses { get; }

        public string Disassembly => FormatDisassembly();

        private string FormatDisassembly()
        {
            if (Instruction.Length <= 1)
            {
                return $"ROM0:{Address:X4}\t0x{Instruction.Value:X2}\t{Instruction.Name}\t\t\t{Instruction.Length}";
            }

            if (Instruction.Length == 2)
            {
                return $"ROM0:{Address:X4}\t0x{Instruction.Value:X2}\t{Instruction.Name}, 0x{Operand1:X2}\t\t{Instruction.Length}";
            }

            ushort immediate = ByteOp.Concat(Operand1, Operand2);
            return $"ROM0:{Address:X4}\t0x{Instruction.Value:X2}\t{Instruction.Name}, 0x{immediate:X2}\t\t{Instruction.Length}";
        }
    }
}
