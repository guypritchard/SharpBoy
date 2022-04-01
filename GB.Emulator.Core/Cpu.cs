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
      catch (Exception e)
      {
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

      throw new ArgumentOutOfRangeException($"0x{instruction.ToString("X2")} not implemented.");
    }

    public Dictionary<byte, Instruction> instructions = new Dictionary<byte, Instruction>()
        {

            { 0x00, new Instruction(0x0, "NOP", (p1, p2) => { }, 1) },
            { 0x05, new Instruction(0x05, "DEC B", (p1, p2) => 
              {
                // This is a subtract operation so set the 'subtract' flag.
                Cpu.Flags.N = true;

                // Set the half carry flag if we're going to 'carry the one' from the ones to the tens unit in hex.
                Cpu.Flags.H = (Cpu.Registers.B & 0x0F) == 0;
                
                Cpu.Registers.B -= 1;

                // Set the 'zero' flag if the value is indeed now zero.
                Cpu.Flags.Z = Cpu.Registers.B == 0;
              },
              1) 
            },
            { 0x06, new Instruction(0x06, "LD C", (p1, p2) => { Cpu.Registers.C = p1; }, 2) },
            { 0x0E, new Instruction(0x0E, "LD D", (p1, p2) => { Cpu.Registers.D = p1;}, 2) },
            { 0x10, new Instruction(0x10, "STOP", (p1, p2) => { Environment.Exit(-1); }, 1)},
            { 0x20, new Instruction(0x20, "JR NZ", (p1, p2) =>
              {
                if (Cpu.Flags.Z == false) {
                  // Convert to signed 8 bit value.
                  var offset = (ushort)((p1 > 0x7F) ? p1 - 0xFF : p1);
                  Cpu.Registers.PC += offset;
                }
              },
              2)
            },
            { 0x21, new Instruction(0x21, $"LD(HL)", (p1, p2) =>
              {
                  Cpu.Registers.H = p2;
                  Cpu.Registers.L = p1;
              },
              3)
            },
            {0x31, new Instruction(0x31, $"LD(SP)", (p1, p2) =>
              {
                  Cpu.Registers.SP = ByteOp.Concat(p1, p2);
              },
              3)
            },
            { 0x32, new Instruction(0x32, "LD(HL-),A", (p1, p2) => 
              {
                Cpu.Memory.Write(Cpu.Registers.A, Cpu.Registers.HL);
                Cpu.Registers.HL--;
              },
              1)
            },
            { 0x76, new Instruction(0x76, "HALT", (p1, p2) => { Environment.Exit(-1); }, 1)},
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
            { 0xC3, new Instruction(0xC3, $"JP", (p1, p2) =>
              {
                  Cpu.Registers.PC = ByteOp.Concat(p1, p2);
              },
              3, 
              false)
            },
            {
              0xEA, new Instruction(0xEA, $"LD A", (p1, p2) => {
                Cpu.Memory.Write(Cpu.Registers.A, ByteOp.Concat(p1, p2));
              },
              3)
            },
            {
                0xF3, new Instruction(0xF3, "DI", (p1, p2) => { }, 1)
            }
        };
    public readonly Video video;
  }
}
