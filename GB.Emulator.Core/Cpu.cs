﻿using System;
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

    private Instruction GetInstruction(byte instruction)
    {
      if (instructions.ContainsKey(instruction))
      {
        return instructions[instruction];
      }

      throw new ArgumentOutOfRangeException($"0x{instruction.ToString("X2")} not implemented at {Cpu.Registers.PC:X4}.");
    }
    
    public readonly Video video;
  }
}
