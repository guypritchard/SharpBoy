using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        private static KeyValuePair<byte, Instruction> Instr(byte opcode, string name, ushort length, Action<byte, byte> handler, bool incrementPc = true)
        {
            return new(opcode, new Instruction(opcode, name, handler, length, incrementPc));
        }

        private static KeyValuePair<byte, Instruction> Dec(byte opcode, string registerName, Func<byte> getter, Action<byte> setter)
        {
            return Instr(opcode, $"DEC {registerName}", 1, (p1, p2) => setter(Operations.Decrement(getter())));
        }

        private static KeyValuePair<byte, Instruction> Dec(byte opcode, string registerName, Func<ushort> getter, Action<ushort> setter)
        {
            return Instr(opcode, $"DEC {registerName}", 1, (p1, p2) => setter(Operations.Decrement(getter())));
        }

        private static KeyValuePair<byte, Instruction> Inc(byte opcode, string registerName, Func<byte> getter, Action<byte> setter)
        {
            return Instr(opcode, $"INC {registerName}", 1, (p1, p2) => setter(Operations.Increment(getter())));
        }

        private static KeyValuePair<byte, Instruction> Inc(byte opcode, string registerName, Func<ushort> getter, Action<ushort> setter)
        {
            return Instr(opcode, $"INC {registerName}", 1, (p1, p2) => setter(Operations.Increment(getter())));
        }

        private static KeyValuePair<byte, Instruction> LdImmediate(byte opcode, string name, Action<byte> setter)
        {
            return Instr(opcode, name, 2, (p1, p2) => setter(p1));
        }

        private static KeyValuePair<byte, Instruction> XorZero(byte opcode, string name, Action<byte> setter)
        {
            return Instr(opcode, name, 1, (p1, p2) => setter(0x0));
        }

        private static KeyValuePair<byte, Instruction> OrRegister(byte opcode, string name, Func<byte> getter)
        {
            return Instr(opcode, name, 1, (p1, p2) => Cpu.Registers.A = Operations.Or(Cpu.Registers.A, getter()));
        }

        private static KeyValuePair<byte, Instruction> OrOperand(byte opcode, string name, Func<byte, byte> getter)
        {
            return Instr(opcode, name, 2, (p1, p2) => Cpu.Registers.A = Operations.Or(Cpu.Registers.A, getter(p1)));
        }

        private static readonly KeyValuePair<byte, Instruction>[] LdInstructions = new[]
        {
            LdImmediate(0x06, "LD B", v => Cpu.Registers.B = v),
            LdImmediate(0x0E, "LD C", v => Cpu.Registers.C = v),
            Instr(0x21, "LD(HL)", 3, (p1, p2) =>
            {
                Cpu.Registers.H = p2;
                Cpu.Registers.L = p1;
            }),
            Instr(0x02, "LD (BC), A", 1, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.BC);
            }),
            Instr(0x12, "LD A (DE)", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Memory.Read8(Cpu.Registers.DE);
            }),
            Instr(0x22, "LD(HL+)A", 1, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.HL);
                Cpu.Registers.HL++;
            }),
            Instr(0x2A, "LD A(HL+)", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Memory.Read8(Cpu.Registers.HL);
                Cpu.Registers.HL++;
            }),
            Instr(0x31, "LD(SP)", 3, (p1, p2) =>
            {
                Cpu.Registers.SP = ByteOp.Concat(p1, p2);
            }),
            Instr(0x32, "LD(HL-)A", 1, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.HL);
                Cpu.Registers.HL--;
            }),
            Instr(0x3E, "LD A, d8", 2, (p1, p2) =>
            {
                Cpu.Registers.A = p1;
            }),
            Instr(0x43, "LD B, E", 1, (p1, p2) => Cpu.Registers.B = Cpu.Registers.E),
            Instr(0x44, "LD B, H", 1, (p1, p2) => Cpu.Registers.B = Cpu.Registers.H),
            Instr(0x4D, "LD C, L", 1, (p1, p2) => Cpu.Registers.C = Cpu.Registers.L),
            Instr(0x54, "LD D, H", 1, (p1, p2) => Cpu.Registers.D = Cpu.Registers.H),
            Instr(0x5D, "LD E, L", 1, (p1, p2) => Cpu.Registers.E = Cpu.Registers.L),
            Instr(0x5F, "LD E, A", 1, (p1, p2) => Cpu.Registers.E = Cpu.Registers.A),
            Instr(0x66, "LD H, (HL)", 1, (p1, p2) =>
            {
                Cpu.Registers.H = Cpu.Memory.Read8(Cpu.Registers.HL);
            }),
            Instr(0x67, "LD L, (HL)", 1, (p1, p2) =>
            {
                Cpu.Registers.L = Cpu.Memory.Read8(Cpu.Registers.HL);
            }),
            Instr(0x68, "LD L, B", 1, (p1, p2) => Cpu.Registers.L = Cpu.Registers.B),
            Instr(0x69, "LD L, C", 1, (p1, p2) => Cpu.Registers.L = Cpu.Registers.C),
            Instr(0x6F, "LD L, A", 1, (p1, p2) => Cpu.Registers.L = Cpu.Registers.A),
            Instr(0x7A, "LD A, D", 1, (p1, p2) => Cpu.Registers.A = Cpu.Registers.D),
            Instr(0x7B, "LD A, E", 1, (p1, p2) => Cpu.Registers.A = Cpu.Registers.E),
            Instr(0x7C, "LD A, H", 1, (p1, p2) => Cpu.Registers.A = Cpu.Registers.H),
            Instr(0x7D, "LD A, L", 1, (p1, p2) => Cpu.Registers.A = Cpu.Registers.L),
            Instr(0x7E, "LD A, (HL)", 1, (p1, p2) => Cpu.Registers.A = Memory.Read8(Cpu.Registers.HL)),
            Instr(0xE0, "LD (a8), A", 2, (p1, p2) =>
            {
                ushort memory = (ushort)(p1 + 0xFF00);
                Cpu.Memory.Write8(Cpu.Registers.A, memory);
            }),
            Instr(0xE2, "LD C A", 1, (p1, p2) =>
            {
                ushort memory = (ushort)(Cpu.Registers.C + 0xFF00);
                Cpu.Memory.Write8(Cpu.Registers.A, memory);
            }),
            Instr(0xEA, "LD A", 3, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, ByteOp.Concat(p1, p2));
            }),
            Instr(0xF0, "LD A, (a8)", 2, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Memory.Read8((ushort)(0xFF00 + p1));
            }),
        };

        private static readonly KeyValuePair<byte, Instruction>[] NoOpLoadInstructions = new[]
        {
            Instr(0x40, "LD B, B", 1, (p1, p2) => { }),
            Instr(0x49, "LD C, C", 1, (p1, p2) => { }),
            Instr(0x52, "LD D, D", 1, (p1, p2) => { }),
            Instr(0x5B, "LD E, E", 1, (p1, p2) => { }),
            Instr(0x64, "LD H, H", 1, (p1, p2) => { }),
            Instr(0x6D, "LD L, L", 1, (p1, p2) => { }),
            Instr(0x7F, "LD A, A", 1, (p1, p2) => { }),
        };

        private static readonly KeyValuePair<byte, Instruction>[] AddInstructions = new[]
        {
            Instr(0x29, "ADD HL HL", 1, (p1, p2) =>
            {
                Cpu.Registers.HL = (ushort)Cpu.Operations.Add(Cpu.Registers.HL, Cpu.Registers.HL);
            }),
            Instr(0x39, "ADD HL SP", 1, (p1, p2) =>
            {
                Cpu.Registers.HL += Cpu.Registers.SP;
            }),
            Instr(0x87, "ADD A, A", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Operations.Add(Cpu.Registers.A, Cpu.Registers.A);
            }),
            Instr(0xE8, "ADD SP, d8", 2, (p1, p2) =>
            {
                Cpu.Registers.SP += (ushort)(sbyte)p1;
            }),
        };

        private static readonly KeyValuePair<byte, Instruction>[] SubInstructions = new[]
        {
            Instr(0x95, "SUB A, L", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Operations.Subtract(Cpu.Registers.A, Cpu.Registers.L);
            }),
            Instr(0x97, "SUB A, A", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Operations.Subtract(Cpu.Registers.A, Cpu.Registers.A);
            }),
            Instr(0x9C, "SBC A, H", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Operations.SubtractWithCarry(Cpu.Registers.A, Cpu.Registers.H);
            }),
        };

        private static readonly KeyValuePair<byte, Instruction>[] XorInstructions = new[]
        {
            XorZero(0xA8, "XOR B", v => Cpu.Registers.B = v),
            XorZero(0xA9, "XOR C", v => Cpu.Registers.C = v),
            XorZero(0xAA, "XOR D", v => Cpu.Registers.D = v),
            XorZero(0xAB, "XOR E", v => Cpu.Registers.E = v),
            XorZero(0xAC, "XOR H", v => Cpu.Registers.H = v),
            XorZero(0xAD, "XOR L", v => Cpu.Registers.L = v),
            Instr(0xAE, "XOR (HL)", 1, (p1, p2) =>
            {
                Cpu.Registers.HL = 0x0;
            }),
            XorZero(0xAF, "XOR A", v => Cpu.Registers.A = v),
        };

        private static readonly KeyValuePair<byte, Instruction>[] OrInstructions = new[]
        {
            OrRegister(0xB0, "OR B", () => Cpu.Registers.B),
            OrRegister(0xB1, "OR C", () => Cpu.Registers.C),
            OrRegister(0xB2, "OR D", () => Cpu.Registers.D),
            OrRegister(0xB3, "OR E", () => Cpu.Registers.E),
            OrRegister(0xB4, "OR H", () => Cpu.Registers.H),
            OrRegister(0xB5, "OR L", () => Cpu.Registers.L),
            Instr(0xB6, "OR (HL)", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Operations.Or(Cpu.Registers.A, Cpu.Memory.Read8(Cpu.Registers.HL));
            }),
            OrRegister(0xB7, "OR A", () => Cpu.Registers.A),
            OrOperand(0xF6, "OR d8", (p1) => Cpu.Registers.A),
        };

        private static readonly KeyValuePair<byte, Instruction>[] CompareInstructions = new[]
        {
            Instr(0xFE, "CP d8", 2, (p1, p2) =>
            {
                Cpu.Flags.Z = Cpu.Registers.A == p1;
            }),
        };

        private static readonly KeyValuePair<byte, Instruction>[] IncrementDecrementInstructions = new[]
        {
            Inc(0x03, "BC", () => Cpu.Registers.BC, v => Cpu.Registers.BC = v),
            Inc(0x04, "B", () => Cpu.Registers.B, v => Cpu.Registers.B = v),
            Dec(0x05, "B", () => Cpu.Registers.B, v => Cpu.Registers.B = v),
            Inc(0x0C, "C", () => Cpu.Registers.C, v => Cpu.Registers.C = v),
            Dec(0x0D, "C", () => Cpu.Registers.C, v => Cpu.Registers.C = v),
            Inc(0x13, "DE", () => Cpu.Registers.DE, v => Cpu.Registers.DE = v),
            Dec(0x15, "D", () => Cpu.Registers.D, v => Cpu.Registers.D = v),
            Dec(0x1D, "E", () => Cpu.Registers.E, v => Cpu.Registers.E = v),
            Inc(0x23, "HL", () => Cpu.Registers.HL, v => Cpu.Registers.HL = v),
            Dec(0x25, "H", () => Cpu.Registers.H, v => Cpu.Registers.H = v),
            Dec(0x2B, "HL", () => Cpu.Registers.HL, v => Cpu.Registers.HL = v),
            Dec(0x2D, "L", () => Cpu.Registers.L, v => Cpu.Registers.L = v),
            Dec(0x3D, "A", () => Cpu.Registers.A, v => Cpu.Registers.A = v),
            Inc(0x33, "SP", () => Cpu.Registers.SP, v => Cpu.Registers.SP = v),
        };

        private static readonly KeyValuePair<byte, Instruction>[] JumpInstructions = new[]
        {
            Instr(0x20, "JR NZ, s8", 2, (p1, p2) =>
            {
                if (Cpu.Flags.Z == false)
                {
                    Cpu.Registers.PC += (ushort)(ByteOp.ToSignedByte(p1));
                    return;
                }
            }),
            Instr(0x30, "JR NC, s8", 2, (p1, p2) =>
            {
                if (Cpu.Flags.C == false)
                {
                    Cpu.Registers.PC += (ushort)(ByteOp.ToSignedByte(p1));
                    return;
                }
            }),
            Instr(0xC2, "JP NZ", 3, (p1, p2) =>
            {
                if (Cpu.Flags.Z == false)
                {
                    Cpu.Registers.PC = ByteOp.Concat(p1, p2);
                }
                else
                {
                    Cpu.Registers.PC += 3;
                }
            }, incrementPc: false),
            Instr(0xC3, "JP", 3, (p1, p2) =>
            {
                Cpu.Registers.PC = ByteOp.Concat(p1, p2);
            }, incrementPc: false),
            Instr(0xC9, "RET", 1, (p1, p2) =>
            {
                // Pop the previous PC value from the stack and write it to the Program Counter to return to where we were before the CALL instruction.
                Trace.WriteLine(Cpu.Registers.Dump());
                Cpu.Registers.PC = Cpu.Memory.Read16(Cpu.Registers.SP);
                Cpu.Registers.SP += 2;
                Trace.WriteLine(Cpu.Registers.Dump());
            }, incrementPc: false),
            Instr(0xCD, "CALL", 3, (p1, p2) =>
            {

                Trace.WriteLine(Cpu.Registers.Dump());
                // Write the current Program Counter to the SP register location - we'll come back here later - we're calling a function now.
                Cpu.Registers.SP -= 2;
                Cpu.Memory.Write16(Cpu.Registers.PC += 3, Cpu.Registers.SP);
                // Set the Program Counter to be the location of the 'function' we're calling. 
                Cpu.Registers.PC = ByteOp.Concat(p1, p2);
                Trace.WriteLine(Cpu.Registers.Dump());
            }, incrementPc: false),
            Instr(0xD0, "RET NC", 1, (p1, p2) =>
            {
                if (Cpu.Flags.C == false)
                {
                    // Pop the SP value back onto the PC.
                    Trace.WriteLine(Cpu.Registers.Dump());
                    Cpu.Registers.PC = Cpu.Memory.Read16(Cpu.Registers.SP);
                    Cpu.Registers.SP += 2;
                    Trace.WriteLine(Cpu.Registers.Dump());
                }
                else
                {
                    Cpu.Registers.PC += 1;
                }
            }, incrementPc: false),
            Instr(0xD8, "RET C", 1, (p1, p2) =>
            {
                if (Cpu.Flags.C == true)
                {
                    // Pop the SP value back onto the PC.
                    Trace.WriteLine(Cpu.Registers.Dump());
                    Cpu.Registers.PC = Cpu.Memory.Read16(Cpu.Registers.SP);
                    Cpu.Registers.SP += 2;
                    Trace.WriteLine(Cpu.Registers.Dump());
                }
                else
                {
                    Cpu.Registers.PC += 1;
                }
            }, incrementPc: false),
        };

        private static readonly KeyValuePair<byte, Instruction>[] StackInstructions = new[]
        {
            Instr(0xD1, "POP DE", 1, (p1, p2) =>
            {
                ByteOp.Split(Cpu.Memory.Read16(Cpu.Registers.SP), out Cpu.Registers.D, out Cpu.Registers.E);
                Cpu.Registers.SP += 2;
            }),
            Instr(0xE5, "PUSH HL", 1, (p1, p2) =>
            {
                Cpu.Registers.SP -= 1;
                Cpu.memory.Write8(Cpu.Registers.H, Cpu.Registers.SP);
                Cpu.Registers.SP -= 1;
                Cpu.memory.Write8(Cpu.Registers.L, Cpu.Registers.SP);
            }),
            Instr(0xF5, "PUSH AF", 1, (p1, p2) =>
            {
                Cpu.Registers.SP -= 1;
                Cpu.memory.Write8(Cpu.Registers.A, Cpu.Registers.SP);
                Cpu.Registers.SP -= 1;
                Cpu.memory.Write8(Cpu.Registers.F, Cpu.Registers.SP);
            }),
        };

        private static readonly KeyValuePair<byte, Instruction>[] SystemInstructions = new[]
        {
            Instr(0x00, "NOP", 1, (p1, p2) => { }),
            Instr(0x10, "STOP", 1, (p1, p2) => { Environment.Exit(-1); }),
            Instr(0x76, "HALT", 1, (p1, p2) => { Environment.Exit(-1); }),
            Instr(0xF3, "DI", 1, (p1, p2) => { }),
        };

        private Dictionary<byte, Instruction> instructions = new(
            LdInstructions
                .Concat(NoOpLoadInstructions)
                .Concat(AddInstructions)
                .Concat(SubInstructions)
                .Concat(XorInstructions)
                .Concat(OrInstructions)
                .Concat(CompareInstructions)
                .Concat(IncrementDecrementInstructions)
                .Concat(JumpInstructions)
                .Concat(StackInstructions)
                .Concat(SystemInstructions));
    }
}
