using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GB.Emulator.Core
{
  public partial class Cpu
  {
    public Cpu(Memory memory, Video video)
    {
      Cpu.memory = memory;
      this.video = video;
    }

    public void Execute(byte[] data)
    {
      try
      {
        while (Cpu.Registers.PC < data.Length)
        {
          Instruction instruction = GetInstruction(data[Cpu.Registers.PC]);
          if (instruction.Length == 1)
          {
            instruction.Execute(0x0, 0x0);
          }
          else
          {
            instruction.Execute(data[Cpu.Registers.PC + 1], data[Cpu.Registers.PC + 2]);
          }
        }
      }
      catch (ArgumentOutOfRangeException aore) {
        Trace.WriteLine(Cpu.Registers.Dump());
        Trace.WriteLine(Cpu.Flags.Dump());
        Trace.WriteLine(aore.Message);
        throw;
      }
      catch (Exception e)
      {
        Instruction instruction = GetInstruction(data[Cpu.Registers.PC]);
        Trace.WriteLine(instruction.Disassemble());
        Trace.WriteLine(Cpu.Registers.Dump());
        Trace.WriteLine(Cpu.Flags.Dump());
        Trace.WriteLine(e.Message);
        throw;
      }
    }

    public Instruction GetInstruction(byte instruction)
    {
      if (instructions.ContainsKey(instruction))
      {
        return instructions[instruction];
      }

      throw new ArgumentOutOfRangeException($"0x{instruction.ToString("X2")} not implemented at {Cpu.Registers.PC:X4}.");
    }

    public Dictionary<byte, Instruction> instructions = new Dictionary<byte, Instruction>()
        {

            { 0x00, new Instruction(0x00, "NOP", (p1, p2) => { }, 1) },
            { 0x05, new Instruction(0x05, "DEC B", (p1, p2) => Cpu.Registers.B = Cpu.Operations.Decrement(Cpu.Registers.B), 1)},
            { 0x06, new Instruction(0x06, "LD B", (p1, p2) => { Cpu.Registers.B = p1; }, 2) },
            { 0x0C, new Instruction(0x0C, "INC C", (p1, p2) => Cpu.Registers.C = Cpu.Operations.Increment(Cpu.Registers.C), 1)},
            { 0x0D, new Instruction(0x0D, "DEC C", (p1, p2) => Cpu.Registers.C = Cpu.Operations.Decrement(Cpu.Registers.C), 1)},
            { 0x0E, new Instruction(0x0E, "LD C", (p1, p2) => { Cpu.Registers.C = p1;}, 2) },
            { 0x10, new Instruction(0x10, "STOP", (p1, p2) => { Environment.Exit(-1); }, 1)},
            { 0x15, new Instruction(0x15, "DEC D", (p1, p2) => Cpu.Registers.D = Cpu.Operations.Decrement(Cpu.Registers.D), 1)},
            { 0x1D, new Instruction(0x1D, "DEC E", (p1, p2) => Cpu.Registers.E = Cpu.Operations.Decrement(Cpu.Registers.E), 1)},
            { 0x20, new Instruction(0x20, "JR NZ", (p1, p2) =>
              {
                if (Cpu.Flags.Z == false) {
                  Cpu.Registers.PC += (ushort)(ByteOp.ToSignedByte(p1));
                  return;
                }
              },
              2)
            },
            { 0x21, new Instruction(0x21, "LD(HL)", (p1, p2) =>
              {
                  Cpu.Registers.H = p2;
                  Cpu.Registers.L = p1;
              },
              3)
            },
            { 0x22, new Instruction(0x22, "LD(HL+)A", (p1, p2) =>
              {
                  Cpu.Memory.Write(Cpu.Registers.A, Cpu.Registers.HL);
                  Cpu.Registers.HL++;
              },
              1)
            },
            { 0x25, new Instruction(0x25, "DEC H", (p1, p2) => Cpu.Registers.H = Cpu.Operations.Decrement(Cpu.Registers.H), 1)},
            { 0x2A, new Instruction(0x2A, "LD A(HL+)", (p1, p2) =>
              {
                var memory = Memory.Read16(Cpu.Registers.HL);
                ByteOp.Split(memory, out Registers.A, out Registers.A);
                Cpu.Registers.HL++;
              },
              1)
            },
            { 0x2D, new Instruction(0x2D, "DEC L", (p1, p2) => Cpu.Registers.L = Cpu.Operations.Decrement(Cpu.Registers.L), 1)},
            { 0x31, new Instruction(0x31, "LD(SP)", (p1, p2) =>
              {
                  Cpu.Registers.SP = ByteOp.Concat(p1, p2);
              },
              3)
            },
            { 0x32, new Instruction(0x32, "LD(HL-)A", (p1, p2) => 
              {
                Cpu.Memory.Write(Cpu.Registers.A, Cpu.Registers.HL);
                Cpu.Registers.HL--;
              },
              1)
            },
            { 0x3D, new Instruction(0x3D, "DEC A", (p1, p2) => Cpu.Registers.A = Cpu.Operations.Decrement(Cpu.Registers.A), 1)},
            { 0x3E, new Instruction(0xEA, "LD A, d8", (p1, p2) => {
                Cpu.Registers.A = p1;
              },
              2)
            },
            { 0x43, new Instruction(0x3D, "LD B, E", (p1, p2) => Cpu.Registers.B = Cpu.Registers.E, 1)},
            { 0x76, new Instruction(0x76, "HALT", (p1, p2) => { Environment.Exit(-1); }, 1)},
            { 0x87, new Instruction(0x76, "ADD A, A", (p1, p2) => { Cpu.Registers.A = Cpu.Operations.Add(Cpu.Registers.A, Cpu.Registers.A); }, 1) },
            { 0xA8, new Instruction(0xA8, "XOR B", (p1, p2) =>
                {
                    Cpu.Registers.B = 0x0;
                },
                1)
            },
            { 0xA9, new Instruction(0xA9, "XOR C", (p1, p2) =>
                {
                    Cpu.Registers.C = 0x0;
                },
                1)
            },
            { 0xAA, new Instruction(0xAA, "XOR D", (p1, p2) =>
                {
                    Cpu.Registers.D = 0x0;
                },
                1)
            },
            { 0xAB, new Instruction(0xAB, "XOR E", (p1, p2) =>
                {
                    Cpu.Registers.E = 0x0;
                },
                1)
            },
            { 0xAC, new Instruction(0xAC, "XOR H", (p1, p2) =>
                {
                    Cpu.Registers.H = 0x0;
                },
                1)
            },
            { 0xAD, new Instruction(0xAD, "XOR L", (p1, p2) =>
                {
                    Cpu.Registers.L = 0x0;
                },
                1)
            },
            { 0xAE, new Instruction(0xAE, "XOR (HL)", (p1, p2) =>
                {
                    Cpu.Registers.HL = 0x0;
                },
                1)
            },
            { 0xAF, new Instruction(0xAF, "XOR A", (p1, p2) =>
                {
                    Cpu.Registers.A = 0x0;
                },
                1)
            },
            { 0xC3, new Instruction(0xC3, "JP", (p1, p2) =>
              {
                  Cpu.Registers.PC = ByteOp.Concat(p1, p2);
              },
              3, 
              false)
            },
            { 0xC9, new Instruction(0xC9, "RET", (p1, p2)=> {
              // Pop the previous PC value from the stack and write it to the Program Counter to return to where we were before the CALL instruction.
              Trace.WriteLine(Cpu.Registers.Dump());
              Cpu.Registers.PC = Cpu.Memory.Read16(Cpu.Registers.SP);
              Cpu.Registers.SP -= 2;
              Trace.WriteLine(Cpu.Registers.Dump());
            }, 1, false)},
            { 0xCD, new Instruction(0xCD, "CALL", (p1, p2) => {
              
              Trace.WriteLine(Cpu.Registers.Dump());
              // Write the current Program Counter to the SP register location - we'll come back here later - we're calling a function now.
              Cpu.Registers.SP -= 2;
              Cpu.Memory.Write(Cpu.Registers.PC += 3, Cpu.Registers.SP);
              // Set the Program Counter to be the location of the 'function' we're calling. 
              Cpu.Registers.PC = ByteOp.Concat(p1, p2);
              Trace.WriteLine(Cpu.Registers.Dump());
            }, 
            3, 
            false)},
            { 0xE0, new Instruction(0xEA, "LD (a8), A", (p1, p2) => {
                Cpu.Memory.Write(Cpu.Registers.A, p1);
              },
              2)
            },
            { 0xE2, new Instruction(0xE2, "LD C A", (p1, p2) =>
              {
                Cpu.Memory.Write(Cpu.Registers.A, Cpu.Registers.C);
              },
            1
            )
            },
            { 0xEA, new Instruction(0xEA, "LD A", (p1, p2) => {
                Cpu.Memory.Write(Cpu.Registers.A, ByteOp.Concat(p1, p2));
              },
              3)
            },
            { 0xF0, new Instruction(0xEA, "LD A, (a8)", (p1, p2) => {
                Cpu.Registers.A = Cpu.Memory.Read8(p1);
                },
              2)
            },
            { 0xF3, new Instruction(0xF3, "DI", (p1, p2) => { }, 1)}
        };
    public readonly Video video;
  }
}
