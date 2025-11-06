using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        public Cpu(MemoryMap memory, Video video)
        {
            Cpu.memory = memory;
            this.video = video;
            Registers.Reset();
        }

        public void Execute(byte[] data)
        {
            try
            {
                while (Cpu.Registers.PC < ushort.MaxValue)
                {
                    this.ExecuteInstruction(data);
                }
            }
            catch (ArgumentOutOfRangeException aore)
            {
                this.HandleExecutionFailure(aore);
                throw;
            }
            catch (Exception e)
            {
                this.HandleExecutionFailure(data, e);
                throw;
            }
        }

        public CpuStepResult ExecuteNextInstruction(byte[] data)
        {
            try
            {
                return this.ExecuteInstruction(data);
            }
            catch (ArgumentOutOfRangeException aore)
            {
                this.HandleExecutionFailure(aore);
                throw;
            }
            catch (Exception e)
            {
                this.HandleExecutionFailure(data, e);
                throw;
            }
        }

        private Instruction GetInstruction(byte instruction)
        {
            if (instructions.ContainsKey(instruction))
            {
                return instructions[instruction];
            }

            throw new NotImplementedException($"0x{instruction.ToString("X2")} not implemented at 0x{Cpu.Registers.PC:X4}.");
        }

        public readonly Video video;

        private CpuStepResult ExecuteInstruction(byte[] data)
        {
            int pc = Cpu.Registers.PC;
            if (pc >= data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(data), $"Program counter 0x{pc:X4} is outside the loaded cartridge.");
            }

            Instruction instruction = GetInstruction(data[pc]);

            byte parameter1 = 0x0;
            byte parameter2 = 0x0;

            if (instruction.Length > 1)
            {
                if (pc + instruction.Length - 1 >= data.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(data), $"Instruction {instruction.Name} at 0x{pc:X4} requires {instruction.Length - 1} operand bytes.");
                }

                parameter1 = data[pc + 1];
                if (instruction.Length > 2)
                {
                    parameter2 = data[pc + 2];
                }
            }

            instruction.Execute(parameter1, parameter2);
            this.video.Step();

            IReadOnlyCollection<ushort> writes = memory.ConsumeRecentWrites();

            return new CpuStepResult(instruction, (ushort)pc, parameter1, parameter2, writes);
        }

        private void HandleExecutionFailure(ArgumentOutOfRangeException exception)
        {
            Trace.WriteLine(Cpu.Registers.Dump());
            Trace.WriteLine(Cpu.Flags.Dump());
            Trace.WriteLine(exception.Message);
        }

        private void HandleExecutionFailure(byte[] data, Exception exception)
        {
            Instruction instruction = GetInstruction(data[Cpu.Registers.PC]);
            Trace.WriteLine(instruction.Disassemble());
            Trace.WriteLine(Cpu.Registers.Dump());
            Trace.WriteLine(Cpu.Flags.Dump());
            Trace.WriteLine(exception.Message);
        }

        internal CpuStepResult DecodeInstruction(byte[] data, ushort address)
        {
            if (address >= data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(address), $"Address 0x{address:X4} is outside the cartridge.");
            }

            Instruction instruction;
            byte opcode = data[address];
            try
            {
                instruction = GetInstruction(opcode);
            }
            catch (NotImplementedException)
            {
                instruction = new Instruction(opcode, $"DB 0x{opcode:X2}", (p1, p2) => { }, 1);
            }

            byte parameter1 = 0x0;
            byte parameter2 = 0x0;

            if (instruction.Length > 1)
            {
                if (address + instruction.Length - 1 >= data.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(address), $"Instruction {instruction.Name} at 0x{address:X4} does not have enough operand bytes.");
                }

                parameter1 = data[address + 1];
                if (instruction.Length > 2)
                {
                    parameter2 = data[address + 2];
                }
            }

            return new CpuStepResult(instruction, address, parameter1, parameter2);
        }
    }
}
