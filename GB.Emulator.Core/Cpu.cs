using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        public Cpu(Memory memory, Video video)
        {
            this.memory = memory;
            this.video = video;
        }

        public void Execute(byte[] data)
        {
            while (Cpu.Registers.PC < data.Length)
            {
                Instruction instruction = this.GetInstruction(data[Cpu.Registers.PC]);
                Trace.WriteLine(instruction.Name);
                instruction.Execute(data[Cpu.Registers.PC + 1], data[Cpu.Registers.PC + 2]);
            }
        }

        public Instruction GetInstruction(byte instruction)
        {
            if (instructions.ContainsKey(instruction))
            {
                return this.instructions[instruction];
            }

            throw new ArgumentOutOfRangeException($"0x{instruction.ToString("X")} not implemented.");
        }

        public Dictionary<byte, Instruction> instructions = new Dictionary<byte, Instruction>()
        {
            { 0x00, new Instruction(0x0, "NOP", (p1, p2) => { }, 1) },
            { 0x10, new Instruction(0x10, "STOP", (p1, p2) => { Environment.Exit(-1); }, 1)},
            { 0x21, new Instruction(0x21, $"LD 0x{Cpu.Registers.HL:x}", (p1, p2) =>
                {
                    Cpu.Registers.H = p2;
                    Cpu.Registers.L = p1;
                },
                3)
            },
            { 0xAF, new Instruction(0xAF, "XOR A", (p1, p2) => 
                {
                    Cpu.Registers.A = (byte)(Cpu.Registers.A ^ Cpu.Registers.A);
                }, 
                1)
            },
            { 0xC3, new Instruction(0xC3, $"JP 0x{Cpu.Registers.PC:x}", (p1, p2) => 
                {
                    Cpu.Registers.PC = ByteOp.Concat(p1, p2);
                }, 
                0)
            }
        };
        private readonly Memory memory;
        private readonly Video video;
    }
}
