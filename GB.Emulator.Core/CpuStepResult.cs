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
                string operand = $"0x{Operand1:X2}";
                if (Instruction.Value is 0xE0 or 0xF0)
                {
                    ushort address = (ushort)(0xFF00 + Operand1);
                    string? label = GetIoLabel(address);
                    operand = label ?? $"0x{address:X4}";
                }

                string formatted = Instruction.FormatWithOperand(Instruction.Name, operand);
                return $"ROM0:{Address:X4}\t0x{Instruction.Value:X2}\t{formatted}\t\t{Instruction.Length}";
            }

            ushort immediate = ByteOp.Concat(Operand1, Operand2);
            string immediateOperand = $"0x{immediate:X4}";
            string? immediateLabel = GetIoLabel(immediate);
            if (immediateLabel != null)
            {
                immediateOperand = Instruction.Value == 0xEA
                    ? immediateLabel
                    : $"0x{immediate:X4} ({immediateLabel})";
            }

            string immediateFormatted = Instruction.FormatWithOperand(Instruction.Name, immediateOperand);
            return $"ROM0:{Address:X4}\t0x{Instruction.Value:X2}\t{immediateFormatted}\t\t{Instruction.Length}";
        }

        private static string? GetIoLabel(ushort address)
        {
            return address switch
            {
                0xFF44 => "LY",
                0xFF0F => "IF",
                0xFFFF => "IE",
                _ => null
            };
        }
    }
}
