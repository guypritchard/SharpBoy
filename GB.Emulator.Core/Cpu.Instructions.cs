using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        private Dictionary<byte, Instruction> instructions = new()
        {

            { 0x00, new Instruction(0x00, "NOP", (p1, p2) => { }, 1) },
            { 0x05, new Instruction(0x05, "DEC B", (p1, p2) => Cpu.Registers.B = Cpu.Operations.Decrement(Cpu.Registers.B), 1) },
            { 0x06, new Instruction(0x06, "LD B", (p1, p2) => { Cpu.Registers.B = p1; }, 2) },
            { 0x0C, new Instruction(0x0C, "INC C", (p1, p2) => Cpu.Registers.C = Cpu.Operations.Increment(Cpu.Registers.C), 1) },
            { 0x0D, new Instruction(0x0D, "DEC C", (p1, p2) => Cpu.Registers.C = Cpu.Operations.Decrement(Cpu.Registers.C), 1) },
            { 0x0E, new Instruction(0x0E, "LD C", (p1, p2) => { Cpu.Registers.C = p1; }, 2) },
            { 0x10, new Instruction(0x10, "STOP", (p1, p2) => { Environment.Exit(-1); }, 1) },
            { 0x15, new Instruction(0x15, "DEC D", (p1, p2) => Cpu.Registers.D = Cpu.Operations.Decrement(Cpu.Registers.D), 1) },
            { 0x1D, new Instruction(0x1D, "DEC E", (p1, p2) => Cpu.Registers.E = Cpu.Operations.Decrement(Cpu.Registers.E), 1) },
            {
                0x20,
                new Instruction(0x20, "JR NZ", (p1, p2) =>
                {
                    if (Cpu.Flags.Z == false)
                    {
                        Cpu.Registers.PC += (ushort)(ByteOp.ToSignedByte(p1));
                        return;
                    }
                },
              2)
            },
            {
                0x21,
                new Instruction(0x21, "LD(HL)", (p1, p2) =>
                {
                    Cpu.Registers.H = p2;
                    Cpu.Registers.L = p1;
                },
              3)
            },
            {
                0x22,
                new Instruction(0x22, "LD(HL+)A", (p1, p2) =>
                {
                    Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.HL);
                    Cpu.Registers.HL++;
                },
              1)
            },
            { 0x25, new Instruction(0x25, "DEC H", (p1, p2) => Cpu.Registers.H = Cpu.Operations.Decrement(Cpu.Registers.H), 1) },
            {
                0x2A,
                new Instruction(0x2A, "LD A(HL+)", (p1, p2) =>
                {
                    var memory = Memory.Read16(Cpu.Registers.HL);
                    ByteOp.Split(memory, out Registers.A, out Registers.A);
                    Cpu.Registers.HL++;
                },
              1)
            },
            { 0x2D, new Instruction(0x2D, "DEC L", (p1, p2) => Cpu.Registers.L = Cpu.Operations.Decrement(Cpu.Registers.L), 1) },
            {
                0x31,
                new Instruction(0x31, "LD(SP)", (p1, p2) =>
                {
                    Cpu.Registers.SP = ByteOp.Concat(p1, p2);
                },
              3)
            },
            {
                0x32,
                new Instruction(0x32, "LD(HL-)A", (p1, p2) =>
                {
                    Cpu.Memory.Write8(Cpu.Registers.A, Cpu.Registers.HL);
                    Cpu.Registers.HL--;
                },
              1)
            },
            { 0x3D, new Instruction(0x3D, "DEC A", (p1, p2) => Cpu.Registers.A = Cpu.Operations.Decrement(Cpu.Registers.A), 1) },
            {
                0x3E,
                new Instruction(0x3E, "LD A, d8", (p1, p2) => {
                    Cpu.Registers.A = p1;
                },
              2)
            },
            { 0x43, new Instruction(0x43, "LD B, E", (p1, p2) => Cpu.Registers.B = Cpu.Registers.E, 1) },
            { 0x76, new Instruction(0x76, "HALT", (p1, p2) => { Environment.Exit(-1); }, 1) },
            { 0x87, new Instruction(0x87, "ADD A, A", (p1, p2) => { Cpu.Registers.A = Cpu.Operations.Add(Cpu.Registers.A, Cpu.Registers.A); }, 1) },
            {
                0x97,
                new Instruction(0x97, "SUB A, A", (p1, p2) => {
                    Cpu.Registers.A = Cpu.Operations.Subtract(Cpu.Registers.A, Cpu.Registers.A);
                }, 1)
            },
            {
                0xA8,
                new Instruction(0xA8, "XOR B", (p1, p2) =>
                {
                    Cpu.Registers.B = 0x0;
                },
                1)
            },
            {
                0xA9,
                new Instruction(0xA9, "XOR C", (p1, p2) =>
                {
                    Cpu.Registers.C = 0x0;
                },
                1)
            },
            {
                0xAA,
                new Instruction(0xAA, "XOR D", (p1, p2) =>
                {
                    Cpu.Registers.D = 0x0;
                },
                1)
            },
            {
                0xAB,
                new Instruction(0xAB, "XOR E", (p1, p2) =>
                {
                    Cpu.Registers.E = 0x0;
                },
                1)
            },
            {
                0xAC,
                new Instruction(0xAC, "XOR H", (p1, p2) =>
                {
                    Cpu.Registers.H = 0x0;
                },
                1)
            },
            {
                0xAD,
                new Instruction(0xAD, "XOR L", (p1, p2) =>
                {
                    Cpu.Registers.L = 0x0;
                },
                1)
            },
            {
                0xAE,
                new Instruction(0xAE, "XOR (HL)", (p1, p2) =>
                {
                    Cpu.Registers.HL = 0x0;
                },
                1)
            },
            {
                0xAF,
                new Instruction(0xAF, "XOR A", (p1, p2) =>
                {
                    Cpu.Registers.A = 0x0;
                },
                1)
            },
            {
                0xC3,
                new Instruction(0xC3, "JP", (p1, p2) =>
                {
                    Cpu.Registers.PC = ByteOp.Concat(p1, p2);
                },
              3,
              false)
            },
            {
                0xC9,
                new Instruction(0xC9, "RET", (p1, p2) => {
                    // Pop the previous PC value from the stack and write it to the Program Counter to return to where we were before the CALL instruction.
                    Trace.WriteLine(Cpu.Registers.Dump());
                    Cpu.Registers.PC = Cpu.Memory.Read16(Cpu.Registers.SP);
                    Cpu.Registers.SP -= 2;
                    Trace.WriteLine(Cpu.Registers.Dump());
                }, 1, false)
            },
            {
                0xCD,
                new Instruction(0xCD, "CALL", (p1, p2) => {

                    Trace.WriteLine(Cpu.Registers.Dump());
                    // Write the current Program Counter to the SP register location - we'll come back here later - we're calling a function now.
                    Cpu.Registers.SP -= 2;
                    Cpu.Memory.Write16(Cpu.Registers.PC += 3, Cpu.Registers.SP);
                    // Set the Program Counter to be the location of the 'function' we're calling. 
                    Cpu.Registers.PC = ByteOp.Concat(p1, p2);
                    Trace.WriteLine(Cpu.Registers.Dump());
                },
            3,
            false)
            },
            {
                0xD0,
                new Instruction (0xD0, "RET NC", (p1, p2) =>
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
                }, 1, false)
            },
            {
                0xE0,
                new Instruction(0xE0, "LD (a8), A", (p1, p2) => {
                    ushort memory = (ushort) (p1 + 0xFF00);
                    Cpu.Memory.Write8(Cpu.Registers.A, memory);
                },
              2)
            },
            {
                0xE2,
                new Instruction(0xE2, "LD C A", (p1, p2) =>
                {
                    ushort memory = (ushort) (Cpu.Registers.C + 0xFF00);
                    Cpu.Memory.Write8(Cpu.Registers.A, memory);
                },
            1
            )
            },
            {
                0xE8,
                new Instruction(0xE8, "ADD SP, d8", (p1, p2) => {
                    Cpu.Registers.SP += (ushort)(sbyte)p1;
                },
                2)
            },
            {
                0xEA,
                new Instruction(0xEA, "LD A", (p1, p2) => {
                    Cpu.Memory.Write8(Cpu.Registers.A, ByteOp.Concat(p1, p2));
                    Cpu.Memory.Write8(Cpu.Registers.A, ByteOp.Concat(p1, p2));
                },
              3)
            },
            {
                0xF0,
                new Instruction(0xF0, "LD A, (a8)", (p1, p2) => {
                    Cpu.Registers.A = Cpu.Memory.Read8((byte)(0xFF00 + p1));
                },
              2)
            },
            {
                0xF3,
                new Instruction(0xF3, "DI", (p1, p2) => { }, 1)
            },
            {
                0xF5,
                new Instruction(0xF5, "PUSH AF", (p1, p2) => {
                    Cpu.Registers.SP -= 1;
                    Cpu.memory.Write8(Cpu.Registers.A, Cpu.Registers.SP);
                    Cpu.Registers.SP -= 1;
                    Cpu.memory.Write8(Cpu.Registers.F, Cpu.Registers.SP);
                    Cpu.Registers.SP -= 2;
                },
                1)
            },
            {
                0xFE,
                new Instruction(0xFE, "CP d8", (p1, p2) => {
                    Cpu.Flags.Z = Cpu.Registers.A == p1;
                },
              2)
            },
        };
    }
}
