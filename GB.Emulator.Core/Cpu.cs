using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        private bool interruptMasterEnabled;
        private int enableInterruptsDelay;
        private bool halted;
        private bool stopped;
        private bool haltBug;

        public Cpu(MemoryMap memory, Video video)
        {
            Cpu.memory = memory;
            this.video = video;
            this.instructions = this.BuildInstructions();
            Registers.Reset();
        }

        public void Execute(byte[] data)
        {
            try
            {
                while (Cpu.Registers.PC < ushort.MaxValue)
                {
                    if (this.halted || this.stopped)
                    {
                        break;
                    }
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
            if (TryGetInstruction(instruction, out Instruction result))
            {
                return result;
            }

            throw new NotImplementedException($"0x{instruction.ToString("X2")} not implemented at 0x{Cpu.Registers.PC:X4}.");
        }

        private bool TryGetInstruction(byte instruction, out Instruction result)
        {
            return instructions.TryGetValue(instruction, out result);
        }

        public readonly Video video;

        internal void ResetExecutionState()
        {
            this.interruptMasterEnabled = false;
            this.enableInterruptsDelay = 0;
            this.halted = false;
            this.stopped = false;
            this.haltBug = false;
        }

        internal void WakeFromStop() => this.stopped = false;

        internal CpuExecutionState CaptureExecutionState() =>
            new(this.interruptMasterEnabled, this.enableInterruptsDelay, this.halted, this.stopped, this.haltBug);

        internal void RestoreExecutionState(CpuExecutionState state)
        {
            this.interruptMasterEnabled = state.InterruptMasterEnabled;
            this.enableInterruptsDelay = state.EnableInterruptsDelay;
            this.halted = state.Halted;
            this.stopped = state.Stopped;
            this.haltBug = state.HaltBug;
        }

        private byte PendingInterrupts =>
            (byte)(Memory.Peek(0xFF0F) & Memory.Peek(0xFFFF) & 0x1F);

        private CpuStepResult ExecuteInstruction(byte[] data)
        {
            byte pending = this.PendingInterrupts;
            if (pending != 0)
            {
                this.halted = false;
                if (this.interruptMasterEnabled)
                {
                    int bit = 0;
                    while ((pending & (1 << bit)) == 0) bit++;
                    ushort vector = (ushort)(0x40 + bit * 8);
                    ushort interruptedPc = Registers.PC;
                    this.interruptMasterEnabled = false;
                    this.stopped = false;
                    Memory.Write8((byte)(Memory.Peek(0xFF0F) & ~(1 << bit)), 0xFF0F);
                    PushWord(interruptedPc);
                    Registers.PC = vector;
                    this.AdvanceVideo(20);
                    var interrupt = new Instruction(0, $"INT 0x{vector:X2}", (_, _) => { }, 0, false);
                    return new CpuStepResult(interrupt, interruptedPc, 0, 0,
                        memory.ConsumeRecentWrites(), memory.ConsumeRecentReads());
                }
            }

            if (this.halted || this.stopped)
            {
                if (this.halted) this.AdvanceVideo();
                var idle = new Instruction(0, this.halted ? "HALT idle" : "STOP idle", (_, _) => { }, 0, false);
                return new CpuStepResult(idle, Registers.PC, 0, 0,
                    memory.ConsumeRecentWrites(), memory.ConsumeRecentReads());
            }

            int pc = Cpu.Registers.PC;
            byte opcode = this.ReadInstructionByte(data, pc);
            Instruction instruction = GetInstruction(opcode);

            byte parameter1 = 0x0;
            byte parameter2 = 0x0;

            bool applyHaltBug = this.haltBug;
            this.haltBug = false;
            if (instruction.Length > 1)
            {
                int lastOperandAddress = pc + instruction.Length - (applyHaltBug ? 2 : 1);
                if (lastOperandAddress > ushort.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(data), $"Instruction {instruction.Name} at 0x{pc:X4} crosses the end of memory.");
                }

                parameter1 = this.ReadInstructionByte(data, pc + (applyHaltBug ? 0 : 1));
                if (instruction.Length > 2)
                {
                    parameter2 = this.ReadInstructionByte(data, pc + (applyHaltBug ? 1 : 2));
                }
            }

            if (applyHaltBug) Registers.PC = (ushort)(pc - 1);
            int cycles = GetCycles(opcode, parameter1);
            bool enableInterruptsNow = this.enableInterruptsDelay == 1;
            instruction.Execute(parameter1, parameter2);
            if (enableInterruptsNow && instruction.Value != 0xF3)
            {
                this.interruptMasterEnabled = true;
                this.enableInterruptsDelay = 0;
            }
            else if (instruction.Value == 0xFB)
            {
                this.enableInterruptsDelay = 1;
            }
            else if (this.enableInterruptsDelay > 0)
            {
                this.enableInterruptsDelay--;
            }
            this.AdvanceVideo(cycles);

            IReadOnlyCollection<ushort> writes = memory.ConsumeRecentWrites();
            IReadOnlyCollection<ushort> reads = memory.ConsumeRecentReads();

            Instruction resultInstruction = instruction.Value == 0xCB
                ? new Instruction(0xCB, GetCbName(parameter1), (_, _) => { }, 2)
                : instruction;
            return new CpuStepResult(resultInstruction, (ushort)pc, parameter1, parameter2, writes, reads);
        }

        private byte ReadInstructionByte(byte[] data, int address)
        {
            if (address < 0 || address > ushort.MaxValue ||
                (address < 0x8000 && address >= data.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(data), $"Program counter 0x{address:X4} is outside the loaded cartridge.");
            }

            // Cartridge instructions come from ROM; code copied into work or high RAM
            // must be fetched through the memory map (for example, DMA routines).
            return address < 0x8000 ? data[address] : Memory.Read8((ushort)address);
        }

        private void AdvanceVideo(int cycles = 4)
        {
            byte interruptRequest = this.video.Step(cycles);
            if (interruptRequest != 0)
            {
                byte flags = Cpu.Memory.Peek(0xFF0F);
                Cpu.Memory.Write8((byte)(flags | interruptRequest), 0xFF0F);
            }
        }

        internal readonly record struct CpuExecutionState(
            bool InterruptMasterEnabled,
            int EnableInterruptsDelay,
            bool Halted,
            bool Stopped,
            bool HaltBug);

        private void HandleExecutionFailure(ArgumentOutOfRangeException exception)
        {
            Trace.WriteLine(Cpu.Registers.Dump());
            Trace.WriteLine(Cpu.Flags.Dump());
            Trace.WriteLine(exception.Message);
        }

        private void HandleExecutionFailure(byte[] data, Exception exception)
        {
            Instruction instruction;
            byte opcode = this.ReadInstructionByte(data, Cpu.Registers.PC);
            if (!TryGetInstruction(opcode, out instruction))
            {
                instruction = new Instruction(opcode, $"NOTIMPL 0x{opcode:X2}", (p1, p2) => { }, 1);
            }
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
            if (!TryGetInstruction(opcode, out instruction))
            {
                instruction = new Instruction(opcode, $"NOTIMPL 0x{opcode:X2}", (p1, p2) => { }, 1);
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

            if (opcode == 0xCB)
            {
                instruction = new Instruction(0xCB, GetCbName(parameter1), (_, _) => { }, 2);
            }

            return new CpuStepResult(instruction, address, parameter1, parameter2);
        }
    }
}
