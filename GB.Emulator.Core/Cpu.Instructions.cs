using System;
using System.Collections.Generic;
using System.Diagnostics;

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

        private static KeyValuePair<byte, Instruction> Inc(byte opcode, string registerName, Func<byte> getter, Action<byte> setter)
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

        private Dictionary<byte, Instruction> instructions = new(new[]
        {
            Instr(0x00, "NOP", 1, (p1, p2) => { }),
            Dec(0x05, "B", () => Cpu.Registers.B, v => Cpu.Registers.B = v),
            LdImmediate(0x06, "LD B", v => Cpu.Registers.B = v),
            Inc(0x0C, "C", () => Cpu.Registers.C, v => Cpu.Registers.C = v),
            Dec(0x0D, "C", () => Cpu.Registers.C, v => Cpu.Registers.C = v),
            LdImmediate(0x0E, "LD C", v => Cpu.Registers.C = v),
            Instr(0x10, "STOP", 1, (p1, p2) => { Environment.Exit(-1); }),
            Dec(0x15, "D", () => Cpu.Registers.D, v => Cpu.Registers.D = v),
            Dec(0x1D, "E", () => Cpu.Registers.E, v => Cpu.Registers.E = v),
            Instr(0x20, "JR NZ", 2, (p1, p2) =>
            {
                if (Cpu.Flags.Z == false)
                {
                    Cpu.Registers.PC += (ushort)(ByteOp.ToSignedByte(p1));
                    return;
                }
            }),
            Instr(0x21, "LD(HL)", 3, (p1, p2) =>
            {
                Cpu.Registers.H = p2;
                Cpu.Registers.L = p1;
            }),
            Instr(0x22, "LD(HL+)A", 1, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.HL);
                Cpu.Registers.HL++;
            }),
            Dec(0x25, "H", () => Cpu.Registers.H, v => Cpu.Registers.H = v),
            Instr(0x2A, "LD A(HL+)", 1, (p1, p2) =>
            {
                var memory = Memory.Read16(Cpu.Registers.HL);
                ByteOp.Split(memory, out Registers.A, out Registers.A);
                Cpu.Registers.HL++;
            }),
            Dec(0x2D, "L", () => Cpu.Registers.L, v => Cpu.Registers.L = v),
            Instr(0x31, "LD(SP)", 3, (p1, p2) =>
            {
                Cpu.Registers.SP = ByteOp.Concat(p1, p2);
            }),
            Instr(0x32, "LD(HL-)A", 1, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.HL);
                Cpu.Registers.HL--;
            }),
            Dec(0x3D, "A", () => Cpu.Registers.A, v => Cpu.Registers.A = v),
            Instr(0x3E, "LD A, d8", 2, (p1, p2) =>
            {
                Cpu.Registers.A = p1;
            }),
            Instr(0x43, "LD B, E", 1, (p1, p2) => Cpu.Registers.B = Cpu.Registers.E),
            Instr(0x76, "HALT", 1, (p1, p2) => { Environment.Exit(-1); }),
            Instr(0x87, "ADD A, A", 1, (p1, p2) => { Cpu.Registers.A = Cpu.Operations.Add(Cpu.Registers.A, Cpu.Registers.A); }),
            Instr(0x97, "SUB A, A", 1, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Operations.Subtract(Cpu.Registers.A, Cpu.Registers.A);
            }),
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
            Instr(0xC3, "JP", 3, (p1, p2) =>
            {
                Cpu.Registers.PC = ByteOp.Concat(p1, p2);
            }, incrementPc: false),
            Instr(0xC9, "RET", 1, (p1, p2) =>
            {
                // Pop the previous PC value from the stack and write it to the Program Counter to return to where we were before the CALL instruction.
                Trace.WriteLine(Cpu.Registers.Dump());
                Cpu.Registers.PC = Cpu.Memory.Read16(Cpu.Registers.SP);
                Cpu.Registers.SP -= 2;
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
            Instr(0xE8, "ADD SP, d8", 2, (p1, p2) =>
            {
                Cpu.Registers.SP += (ushort)(sbyte)p1;
            }),
            Instr(0xEA, "LD A", 3, (p1, p2) =>
            {
                Cpu.Memory.Write8(Cpu.Registers.A, ByteOp.Concat(p1, p2));
                Cpu.Memory.Write8(Cpu.Registers.A, ByteOp.Concat(p1, p2));
            }),
            Instr(0xF0, "LD A, (a8)", 2, (p1, p2) =>
            {
                Cpu.Registers.A = Cpu.Memory.Read8((byte)(0xFF00 + p1));
            }),
            Instr(0xF3, "DI", 1, (p1, p2) => { }),
            Instr(0xF5, "PUSH AF", 1, (p1, p2) =>
            {
                Cpu.Registers.SP -= 1;
                Cpu.memory.Write8(Cpu.Registers.A, Cpu.Registers.SP);
                Cpu.Registers.SP -= 1;
                Cpu.memory.Write8(Cpu.Registers.F, Cpu.Registers.SP);
                Cpu.Registers.SP -= 2;
            }),
            Instr(0xFE, "CP d8", 2, (p1, p2) =>
            {
                Cpu.Flags.Z = Cpu.Registers.A == p1;
            }),
        });
    }
}
