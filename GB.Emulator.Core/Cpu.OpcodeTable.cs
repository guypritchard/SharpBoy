using System;
using System.Collections.Generic;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        private static readonly string[] RegisterNames = { "B", "C", "D", "E", "H", "L", "(HL)", "A" };
        private static readonly string[] PairNames = { "BC", "DE", "HL", "SP" };
        private static readonly string[] ConditionNames = { "NZ", "Z", "NC", "C" };
        private static readonly byte[] FirstQuarterCycles =
        {
             4, 12,  8,  8,  4,  4,  8,  4, 20,  8,  8,  8,  4,  4,  8,  4,
             4, 12,  8,  8,  4,  4,  8,  4, 12,  8,  8,  8,  4,  4,  8,  4,
             8, 12,  8,  8,  4,  4,  8,  4,  8,  8,  8,  8,  4,  4,  8,  4,
             8, 12,  8,  8, 12, 12, 12,  4,  8,  8,  8,  8,  4,  4,  8,  4
        };

        private static int GetCycles(byte opcode, byte operand)
        {
            if (opcode < 0x40)
            {
                if (opcode is 0x20 or 0x28 or 0x30 or 0x38)
                    return Condition((opcode - 0x20) / 8) ? 12 : 8;
                return FirstQuarterCycles[opcode];
            }

            if (opcode < 0x80)
                return opcode == 0x76 ? 4 : ((opcode & 7) == 6 || ((opcode >> 3) & 7) == 6 ? 8 : 4);
            if (opcode < 0xC0)
                return (opcode & 7) == 6 ? 8 : 4;

            if (opcode is 0xC0 or 0xC8 or 0xD0 or 0xD8)
                return Condition((opcode - 0xC0) / 8) ? 20 : 8;
            if (opcode is 0xC2 or 0xCA or 0xD2 or 0xDA)
                return Condition((opcode - 0xC2) / 8) ? 16 : 12;
            if (opcode is 0xC4 or 0xCC or 0xD4 or 0xDC)
                return Condition((opcode - 0xC4) / 8) ? 24 : 12;
            if (opcode == 0xCB)
                return (operand & 7) == 6 ? (operand >= 0x40 && operand < 0x80 ? 12 : 16) : 8;

            return opcode switch
            {
                0xC1 or 0xD1 or 0xE1 or 0xF1 => 12,
                0xC5 or 0xD5 or 0xE5 or 0xF5 => 16,
                0xC7 or 0xCF or 0xD7 or 0xDF or 0xE7 or 0xEF or 0xF7 or 0xFF => 16,
                0xC9 or 0xD9 => 16,
                0xC3 or 0xEA or 0xFA or 0xE8 => 16,
                0xCD => 24,
                0xE0 or 0xF0 or 0xF8 => 12,
                0xE2 or 0xF2 or 0xF9 => 8,
                0xE9 or 0xF3 or 0xFB => 4,
                0xC6 or 0xCE or 0xD6 or 0xDE or 0xE6 or 0xEE or 0xF6 or 0xFE => 8,
                _ => throw new InvalidOperationException($"No cycle count for opcode 0x{opcode:X2}.")
            };
        }

        private Dictionary<byte, Instruction> BuildInstructions()
        {
            var table = new Dictionary<byte, Instruction>();

            void Add(int opcode, string name, int length, Action<byte, byte> action, bool incrementPc = true) =>
                table.Add((byte)opcode, new Instruction((byte)opcode, name, action, (ushort)length, incrementPc));

            Add(0x00, "NOP", 1, (_, _) => { });
            Add(0x08, "LD (a16), SP", 3, (lo, hi) => Memory.Write16(Registers.SP, ByteOp.Concat(lo, hi)));
            Add(0x10, "STOP", 2, (_, _) => this.stopped = true);
            Add(0x18, "JR s8", 2, (offset, _) => JumpRelative(offset), false);
            Add(0x27, "DAA", 1, (_, _) => DecimalAdjust());
            Add(0x2F, "CPL", 1, (_, _) => { Registers.A = (byte)~Registers.A; Flags.N = true; Flags.H = true; });
            Add(0x37, "SCF", 1, (_, _) => { Flags.N = false; Flags.H = false; Flags.C = true; });
            Add(0x3F, "CCF", 1, (_, _) => { bool carry = Flags.C; Flags.N = false; Flags.H = false; Flags.C = !carry; });

            // The first 64 opcodes mostly follow an eight-opcode register-pair pattern.
            for (int pair = 0; pair < 4; pair++)
            {
                int p = pair;
                int baseOpcode = pair * 0x10;
                Add(baseOpcode + 0x01, $"LD {PairNames[p]}, d16", 3,
                    (lo, hi) => SetPair(p, ByteOp.Concat(lo, hi)));
                Add(baseOpcode + 0x03, $"INC {PairNames[p]}", 1,
                    (_, _) => SetPair(p, (ushort)(GetPair(p) + 1)));
                Add(baseOpcode + 0x09, $"ADD HL, {PairNames[p]}", 1,
                    (_, _) => AddHl(GetPair(p)));
                Add(baseOpcode + 0x0B, $"DEC {PairNames[p]}", 1,
                    (_, _) => SetPair(p, (ushort)(GetPair(p) - 1)));

                int r1 = pair * 2;
                int r2 = r1 + 1;
                Add(baseOpcode + 0x04, $"INC {RegisterNames[r1]}", 1,
                    (_, _) => WriteRegister(r1, Operations.Increment(ReadRegister(r1))));
                Add(baseOpcode + 0x05, $"DEC {RegisterNames[r1]}", 1,
                    (_, _) => WriteRegister(r1, Operations.Decrement(ReadRegister(r1))));
                Add(baseOpcode + 0x06, $"LD {RegisterNames[r1]}, d8", 2,
                    (value, _) => WriteRegister(r1, value));
                Add(baseOpcode + 0x0C, $"INC {RegisterNames[r2]}", 1,
                    (_, _) => WriteRegister(r2, Operations.Increment(ReadRegister(r2))));
                Add(baseOpcode + 0x0D, $"DEC {RegisterNames[r2]}", 1,
                    (_, _) => WriteRegister(r2, Operations.Decrement(ReadRegister(r2))));
                Add(baseOpcode + 0x0E, $"LD {RegisterNames[r2]}, d8", 2,
                    (value, _) => WriteRegister(r2, value));
            }

            Add(0x02, "LD (BC), A", 1, (_, _) => Memory.Write8(Registers.A, Registers.BC));
            Add(0x0A, "LD A, (BC)", 1, (_, _) => Registers.A = Memory.Read8(Registers.BC));
            Add(0x12, "LD (DE), A", 1, (_, _) => Memory.Write8(Registers.A, Registers.DE));
            Add(0x1A, "LD A, (DE)", 1, (_, _) => Registers.A = Memory.Read8(Registers.DE));
            Add(0x22, "LD (HL+), A", 1, (_, _) => { Memory.Write8(Registers.A, Registers.HL); Registers.HL++; });
            Add(0x2A, "LD A, (HL+)", 1, (_, _) => { Registers.A = Memory.Read8(Registers.HL); Registers.HL++; });
            Add(0x32, "LD (HL-), A", 1, (_, _) => { Memory.Write8(Registers.A, Registers.HL); Registers.HL--; });
            Add(0x3A, "LD A, (HL-)", 1, (_, _) => { Registers.A = Memory.Read8(Registers.HL); Registers.HL--; });

            Add(0x07, "RLCA", 1, (_, _) => { Registers.A = Operations.RotateLeftCircular(Registers.A); Flags.Z = false; });
            Add(0x0F, "RRCA", 1, (_, _) => { Registers.A = Operations.RotateRightCircular(Registers.A); Flags.Z = false; });
            Add(0x17, "RLA", 1, (_, _) => { Registers.A = Operations.RotateLeft(Registers.A); Flags.Z = false; });
            Add(0x1F, "RRA", 1, (_, _) => { Registers.A = Operations.RotateRight(Registers.A); Flags.Z = false; });

            for (int condition = 0; condition < 4; condition++)
            {
                int c = condition;
                Add(0x20 + condition * 8, $"JR {ConditionNames[c]}, s8", 2,
                    (offset, _) => { if (Condition(c)) JumpRelative(offset); else Registers.PC += 2; }, false);
            }

            for (int destination = 0; destination < 8; destination++)
            {
                for (int source = 0; source < 8; source++)
                {
                    int d = destination;
                    int s = source;
                    int opcode = 0x40 + destination * 8 + source;
                    if (opcode == 0x76)
                    {
                        Add(opcode, "HALT", 1, (_, _) =>
                        {
                            if (!this.interruptMasterEnabled && this.PendingInterrupts != 0) this.haltBug = true;
                            else this.halted = true;
                        });
                    }
                    else
                    {
                        Add(opcode, $"LD {RegisterNames[d]}, {RegisterNames[s]}", 1,
                            (_, _) => WriteRegister(d, ReadRegister(s)));
                    }
                }
            }

            string[] arithmeticNames = { "ADD A", "ADC A", "SUB A", "SBC A", "AND", "XOR", "OR", "CP" };
            for (int operation = 0; operation < 8; operation++)
            {
                for (int source = 0; source < 8; source++)
                {
                    int op = operation;
                    int s = source;
                    string name = op < 4
                        ? $"{arithmeticNames[op]}, {RegisterNames[s]}"
                        : $"{arithmeticNames[op]} {RegisterNames[s]}";
                    Add(0x80 + operation * 8 + source, name, 1,
                        (_, _) => Arithmetic(op, ReadRegister(s)));
                }
            }

            for (int condition = 0; condition < 4; condition++)
            {
                int c = condition;
                Add(0xC0 + condition * 8, $"RET {ConditionNames[c]}", 1,
                    (_, _) => { if (Condition(c)) Return(); else Registers.PC++; }, false);
                Add(0xC2 + condition * 8, $"JP {ConditionNames[c]}, a16", 3,
                    (lo, hi) => Registers.PC = Condition(c) ? ByteOp.Concat(lo, hi) : (ushort)(Registers.PC + 3), false);
                Add(0xC4 + condition * 8, $"CALL {ConditionNames[c]}, a16", 3,
                    (lo, hi) => { if (Condition(c)) Call(ByteOp.Concat(lo, hi)); else Registers.PC += 3; }, false);
            }

            for (int pair = 0; pair < 4; pair++)
            {
                int p = pair;
                string pairName = pair == 3 ? "AF" : PairNames[p];
                Add(0xC1 + pair * 0x10, $"POP {pairName}", 1, (_, _) => PopPair(p));
                Add(0xC5 + pair * 0x10, $"PUSH {pairName}", 1, (_, _) => PushPair(p));
            }

            for (int vector = 0; vector < 8; vector++)
            {
                int target = vector * 8;
                Add(0xC7 + vector * 8, $"RST 0x{target:X2}", 1,
                    (_, _) => { PushWord((ushort)(Registers.PC + 1)); Registers.PC = (ushort)target; }, false);
            }

            Add(0xC3, "JP a16", 3, (lo, hi) => Registers.PC = ByteOp.Concat(lo, hi), false);
            Add(0xC9, "RET", 1, (_, _) => Return(), false);
            Add(0xCD, "CALL a16", 3, (lo, hi) => Call(ByteOp.Concat(lo, hi)), false);
            Add(0xD9, "RETI", 1, (_, _) => { Return(); this.interruptMasterEnabled = true; }, false);
            Add(0xE9, "JP (HL)", 1, (_, _) => Registers.PC = Registers.HL, false);

            int[] immediateOpcodes = { 0xC6, 0xCE, 0xD6, 0xDE, 0xE6, 0xEE, 0xF6, 0xFE };
            for (int operation = 0; operation < 8; operation++)
            {
                int op = operation;
                string name = op < 4 ? $"{arithmeticNames[op]}, d8" : $"{arithmeticNames[op]} d8";
                Add(immediateOpcodes[operation], name, 2,
                    (value, _) => Arithmetic(op, value));
            }

            Add(0xCB, "PREFIX CB", 2, (opcode, _) => ExecuteCb(opcode));
            Add(0xE0, "LD (a8), A", 2, (offset, _) => Memory.Write8(Registers.A, (ushort)(0xFF00 + offset)));
            Add(0xE2, "LD (C), A", 1, (_, _) => Memory.Write8(Registers.A, (ushort)(0xFF00 + Registers.C)));
            Add(0xEA, "LD (a16), A", 3, (lo, hi) => Memory.Write8(Registers.A, ByteOp.Concat(lo, hi)));
            Add(0xF0, "LD A, (a8)", 2, (offset, _) => Registers.A = Memory.Read8((ushort)(0xFF00 + offset)));
            Add(0xF2, "LD A, (C)", 1, (_, _) => Registers.A = Memory.Read8((ushort)(0xFF00 + Registers.C)));
            Add(0xFA, "LD A, (a16)", 3, (lo, hi) => Registers.A = Memory.Read8(ByteOp.Concat(lo, hi)));
            Add(0xE8, "ADD SP, s8", 2, (offset, _) => Registers.SP = AddSignedToSp(offset));
            Add(0xF8, "LD HL, SP+s8", 2, (offset, _) => Registers.HL = AddSignedToSp(offset));
            Add(0xF9, "LD SP, HL", 1, (_, _) => Registers.SP = Registers.HL);
            Add(0xF3, "DI", 1, (_, _) => { this.interruptMasterEnabled = false; this.enableInterruptsDelay = 0; });
            Add(0xFB, "EI", 1, (_, _) => this.enableInterruptsDelay = 2);

            return table;
        }

        private static byte ReadRegister(int index) => index switch
        {
            0 => Registers.B, 1 => Registers.C, 2 => Registers.D, 3 => Registers.E,
            4 => Registers.H, 5 => Registers.L, 6 => Memory.Read8(Registers.HL), 7 => Registers.A,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private static void WriteRegister(int index, byte value)
        {
            switch (index)
            {
                case 0: Registers.B = value; break;
                case 1: Registers.C = value; break;
                case 2: Registers.D = value; break;
                case 3: Registers.E = value; break;
                case 4: Registers.H = value; break;
                case 5: Registers.L = value; break;
                case 6: Memory.Write8(value, Registers.HL); break;
                case 7: Registers.A = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static ushort GetPair(int index) => index switch
        {
            0 => Registers.BC, 1 => Registers.DE, 2 => Registers.HL, 3 => Registers.SP,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private static void SetPair(int index, ushort value)
        {
            switch (index)
            {
                case 0: Registers.BC = value; break;
                case 1: Registers.DE = value; break;
                case 2: Registers.HL = value; break;
                case 3: Registers.SP = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        private static bool Condition(int index) => index switch
        {
            0 => !Flags.Z, 1 => Flags.Z, 2 => !Flags.C, 3 => Flags.C,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private static void JumpRelative(byte offset) =>
            Registers.PC = (ushort)(Registers.PC + 2 + (sbyte)offset);

        private static void PushWord(ushort value)
        {
            Registers.SP -= 2;
            Memory.Write16(value, Registers.SP);
        }

        private static ushort PopWord()
        {
            ushort value = Memory.Read16(Registers.SP);
            Registers.SP += 2;
            return value;
        }

        private static void PopPair(int pair)
        {
            ushort value = PopWord();
            if (pair == 3)
            {
                Registers.A = (byte)(value >> 8);
                Registers.Flags = (byte)(value & 0xF0);
            }
            else SetPair(pair, value);
        }

        private static void PushPair(int pair) => PushWord(pair == 3
            ? (ushort)((Registers.A << 8) | Registers.Flags)
            : GetPair(pair));

        private static void Return() => Registers.PC = PopWord();

        private static void Call(ushort address)
        {
            PushWord((ushort)(Registers.PC + 3));
            Registers.PC = address;
        }

        private static void AddHl(ushort value)
        {
            ushort old = Registers.HL;
            int sum = old + value;
            Flags.N = false;
            Flags.H = ((old & 0x0FFF) + (value & 0x0FFF)) > 0x0FFF;
            Flags.C = sum > 0xFFFF;
            Registers.HL = (ushort)sum;
        }

        private static ushort AddSignedToSp(byte offset)
        {
            int signed = (sbyte)offset;
            ushort old = Registers.SP;
            ushort result = (ushort)(old + signed);
            Registers.Flags = 0;
            Flags.H = ((old ^ signed ^ result) & 0x10) != 0;
            Flags.C = ((old ^ signed ^ result) & 0x100) != 0;
            return result;
        }

        private static void DecimalAdjust()
        {
            int value = Registers.A;
            int adjust = 0;
            bool carry = Flags.C;
            if (!Flags.N)
            {
                if (Flags.H || (value & 0x0F) > 9) adjust |= 0x06;
                if (carry || value > 0x99) { adjust |= 0x60; carry = true; }
                value += adjust;
            }
            else
            {
                if (Flags.H) adjust |= 0x06;
                if (carry) adjust |= 0x60;
                value -= adjust;
            }

            Registers.A = (byte)value;
            Flags.Z = Registers.A == 0;
            Flags.H = false;
            Flags.C = carry;
        }

        private static void Arithmetic(int operation, byte value)
        {
            switch (operation)
            {
                case 0: Registers.A = Operations.Add(Registers.A, value); break;
                case 1: Registers.A = Operations.AddWithCarry(Registers.A, value); break;
                case 2: Registers.A = Operations.Subtract(Registers.A, value); break;
                case 3: Registers.A = Operations.SubtractWithCarry(Registers.A, value); break;
                case 4: Registers.A = Operations.And(Registers.A, value); break;
                case 5: Registers.A = Operations.Xor(Registers.A, value); break;
                case 6: Registers.A = Operations.Or(Registers.A, value); break;
                case 7: Operations.Subtract(Registers.A, value); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        internal static string GetCbName(byte opcode)
        {
            int register = opcode & 7;
            int group = opcode >> 6;
            if (group != 0)
            {
                string operation = group == 1 ? "BIT" : group == 2 ? "RES" : "SET";
                return $"{operation} {(opcode >> 3) & 7}, {RegisterNames[register]}";
            }

            string[] names = { "RLC", "RRC", "RL", "RR", "SLA", "SRA", "SWAP", "SRL" };
            return $"{names[(opcode >> 3) & 7]} {RegisterNames[register]}";
        }

        private static void ExecuteCb(byte opcode)
        {
            int register = opcode & 7;
            int bit = (opcode >> 3) & 7;
            int group = opcode >> 6;
            byte value = ReadRegister(register);

            if (group == 1)
            {
                Flags.Z = (value & (1 << bit)) == 0;
                Flags.N = false;
                Flags.H = true;
                return;
            }

            if (group == 2 || group == 3)
            {
                byte updated = group == 2
                    ? (byte)(value & ~(1 << bit))
                    : (byte)(value | (1 << bit));
                WriteRegister(register, updated);
                return;
            }

            int oldCarry = Flags.C ? 1 : 0;
            bool carry;
            byte result;
            switch (bit)
            {
                case 0: carry = (value & 0x80) != 0; result = (byte)((value << 1) | (value >> 7)); break;
                case 1: carry = (value & 1) != 0; result = (byte)((value >> 1) | (value << 7)); break;
                case 2: carry = (value & 0x80) != 0; result = (byte)((value << 1) | oldCarry); break;
                case 3: carry = (value & 1) != 0; result = (byte)((value >> 1) | (oldCarry << 7)); break;
                case 4: carry = (value & 0x80) != 0; result = (byte)(value << 1); break;
                case 5: carry = (value & 1) != 0; result = (byte)((value >> 1) | (value & 0x80)); break;
                case 6: carry = false; result = (byte)((value << 4) | (value >> 4)); break;
                default: carry = (value & 1) != 0; result = (byte)(value >> 1); break;
            }

            WriteRegister(register, result);
            Registers.Flags = (byte)((result == 0 ? 0x80 : 0) | (carry ? 0x10 : 0));
        }
    }
}
