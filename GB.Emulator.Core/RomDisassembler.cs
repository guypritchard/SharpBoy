#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace GB.Emulator.Core
{
    /// <summary>Builds debugger views of the cartridge's directly addressable ROM.</summary>
    internal sealed class RomDisassembler
    {
        private const int RomAddressLimit = 0x8000;
        private readonly Cpu cpu;

        public RomDisassembler(Cpu cpu)
        {
            this.cpu = cpu;
        }

        public IReadOnlyList<CpuStepResult> GetInstructionWindow(
            byte[] romData, ushort programCounter, int instructionsBefore, int instructionsAfter)
        {
            int addressLimit = Math.Min(romData.Length, RomAddressLimit);
            var precedingInstructions = new Queue<CpuStepResult>();
            CpuStepResult? current = null;
            int address = 0;

            while (address < addressLimit)
            {
                CpuStepResult decoded = this.cpu.DecodeInstruction(romData, (ushort)address);
                precedingInstructions.Enqueue(decoded);
                if (precedingInstructions.Count > instructionsBefore + 1)
                {
                    precedingInstructions.Dequeue();
                }

                address = NextAddress(decoded);
                if (decoded.Address == programCounter)
                {
                    current = decoded;
                    break;
                }
            }

            if (current == null)
            {
                return Array.Empty<CpuStepResult>();
            }

            var window = precedingInstructions.ToList();
            for (int i = 0; i < instructionsAfter && address < addressLimit; i++)
            {
                CpuStepResult decoded = this.cpu.DecodeInstruction(romData, (ushort)address);
                window.Add(decoded);
                address = NextAddress(decoded);
            }

            return window;
        }

        public IReadOnlyList<CpuStepResult> GetDisassembly(byte[] romData)
        {
            int addressLimit = Math.Min(romData.Length, RomAddressLimit);
            var instructions = new List<CpuStepResult>();
            int address = 0;

            while (address < addressLimit)
            {
                CpuStepResult decoded;
                try
                {
                    decoded = this.cpu.DecodeInstruction(romData, (ushort)address);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // A cartridge may end partway through an instruction's operands.
                    break;
                }

                instructions.Add(decoded);
                address = NextAddress(decoded);
            }

            return instructions;
        }

        private static int NextAddress(CpuStepResult instruction) =>
            instruction.Address + Math.Max(1, (int)instruction.Instruction.Length);
    }
}
