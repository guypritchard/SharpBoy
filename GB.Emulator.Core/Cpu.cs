using System;
using System.Collections.Generic;
using System.Diagnostics;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public partial class Cpu
    {
        private static readonly Instruction HaltIdleInstruction = new(0, "HALT idle", (_, _) => { }, 0, false);
        private static readonly Instruction StopIdleInstruction = new(0, "STOP idle", (_, _) => { }, 0, false);
        private bool interruptMasterEnabled;
        private int enableInterruptsDelay;
        private bool halted;
        private bool stopped;
        private bool haltBug;
        private long totalCycles;

        public Cpu(MemoryMap memory, Video video, SerialPort serial, Apu apu, Timer timer)
        {
            Cpu.memory = memory;
            this.video = video;
            this.serial = serial;
            this.apu = apu;
            this.timer = timer;
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
                return this.BuildStepResult(this.ExecuteInstruction(data));
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

        internal void ExecuteNextInstructionFast(byte[] data) => this.ExecuteInstruction(data);

        internal bool IsStopped => this.stopped;

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
        private readonly SerialPort serial;
        private readonly Apu apu;
        private readonly Timer timer;
        public long TotalCycles => this.totalCycles;

        internal void ResetExecutionState()
        {
            this.interruptMasterEnabled = false;
            this.enableInterruptsDelay = 0;
            this.halted = false;
            this.stopped = false;
            this.haltBug = false;
            this.totalCycles = 0;
        }

        internal void WakeFromStop() => this.stopped = false;

        internal CpuExecutionState CaptureExecutionState() =>
            new(this.interruptMasterEnabled, this.enableInterruptsDelay, this.halted, this.stopped, this.haltBug,
                this.totalCycles);

        internal void RestoreExecutionState(CpuExecutionState state)
        {
            this.interruptMasterEnabled = state.InterruptMasterEnabled;
            this.enableInterruptsDelay = state.EnableInterruptsDelay;
            this.halted = state.Halted;
            this.stopped = state.Stopped;
            this.haltBug = state.HaltBug;
            this.totalCycles = state.TotalCycles;
        }

        private byte PendingInterrupts =>
            (byte)(Memory.Peek(0xFF0F) & Memory.Peek(0xFFFF) & 0x1F);

        private InstructionExecution ExecuteInstruction(byte[] data)
        {
            if (this.TryServiceInterrupt(out InstructionExecution interruptExecution))
            {
                return interruptExecution;
            }

            if (this.halted || this.stopped)
            {
                if (this.halted)
                {
                    this.AdvanceHardware();
                }

                Instruction idleInstruction = this.halted ? HaltIdleInstruction : StopIdleInstruction;
                return new InstructionExecution(idleInstruction, Registers.PC, 0, 0);
            }

            ushort address = Registers.PC;
            byte opcode = this.ReadInstructionByte(data, address);
            Instruction instruction = this.GetInstruction(opcode);
            InstructionExecution execution = this.ReadOperands(data, address, instruction);
            int cycles = GetCycles(opcode, execution.Operand1);
            bool enableInterruptsNow = this.enableInterruptsDelay == 1;

            instruction.Execute(execution.Operand1, execution.Operand2);
            this.UpdateDelayedInterruptEnable(opcode, enableInterruptsNow);
            this.AdvanceHardware(cycles);

            return execution;
        }

        private bool TryServiceInterrupt(out InstructionExecution execution)
        {
            execution = default;
            byte pending = this.PendingInterrupts;
            if (pending == 0)
            {
                return false;
            }

            // An enabled interrupt wakes HALT even when the CPU cannot service it yet.
            this.halted = false;
            if (!this.interruptMasterEnabled)
            {
                return false;
            }

            int interruptBit = 0;
            while ((pending & (1 << interruptBit)) == 0)
            {
                interruptBit++;
            }

            ushort vector = (ushort)(0x40 + interruptBit * 8);
            ushort interruptedAddress = Registers.PC;
            this.interruptMasterEnabled = false;
            this.stopped = false;
            Memory.Write8((byte)(Memory.Peek(0xFF0F) & ~(1 << interruptBit)), 0xFF0F);
            PushWord(interruptedAddress);
            Registers.PC = vector;
            this.AdvanceHardware(20);

            var interrupt = new Instruction(0, $"INT 0x{vector:X2}", (_, _) => { }, 0, false);
            execution = new InstructionExecution(interrupt, interruptedAddress, 0, 0);
            return true;
        }

        private InstructionExecution ReadOperands(byte[] data, ushort address, Instruction instruction)
        {
            byte operand1 = 0;
            byte operand2 = 0;
            bool applyHaltBug = this.haltBug;
            this.haltBug = false;

            // The HALT bug repeats the opcode byte as the first operand and suppresses
            // one program-counter increment for this instruction.
            int operandOffset = applyHaltBug ? 0 : 1;
            if (instruction.Length > 1)
            {
                int lastOperandAddress = address + instruction.Length - (applyHaltBug ? 2 : 1);
                if (lastOperandAddress > ushort.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(data), $"Instruction {instruction.Name} at 0x{address:X4} crosses the end of memory.");
                }

                operand1 = this.ReadInstructionByte(data, address + operandOffset);
                if (instruction.Length > 2)
                {
                    operand2 = this.ReadInstructionByte(data, address + operandOffset + 1);
                }
            }

            if (applyHaltBug)
            {
                Registers.PC = (ushort)(address - 1);
            }

            return new InstructionExecution(instruction, address, operand1, operand2);
        }

        private void UpdateDelayedInterruptEnable(byte opcode, bool enableInterruptsNow)
        {
            const byte disableInterruptsOpcode = 0xF3;
            const byte enableInterruptsOpcode = 0xFB;

            if (enableInterruptsNow && opcode != disableInterruptsOpcode)
            {
                this.interruptMasterEnabled = true;
                this.enableInterruptsDelay = 0;
            }
            else if (opcode == enableInterruptsOpcode)
            {
                this.enableInterruptsDelay = 1;
            }
            else if (this.enableInterruptsDelay > 0)
            {
                this.enableInterruptsDelay--;
            }
        }

        private CpuStepResult BuildStepResult(InstructionExecution execution)
        {
            Instruction resultInstruction = execution.Instruction.Value == 0xCB
                ? new Instruction(0xCB, GetCbName(execution.Operand1), (_, _) => { }, 2)
                : execution.Instruction;
            return new CpuStepResult(resultInstruction, execution.Address, execution.Operand1, execution.Operand2,
                memory.ConsumeRecentWrites(), memory.ConsumeRecentReads());
        }

        private readonly record struct InstructionExecution(Instruction Instruction, ushort Address,
            byte Operand1, byte Operand2);

        private byte ReadInstructionByte(byte[] data, int address)
        {
            if (address < 0 || address > ushort.MaxValue ||
                (address < 0x8000 && address >= data.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(data), $"Program counter 0x{address:X4} is outside the loaded cartridge.");
            }

            // Cartridge instructions come from ROM; code copied into work or high RAM
            // must be fetched through the memory map (for example, DMA routines).
            return Memory.Read8((ushort)address);
        }

        private void AdvanceHardware(int cycles = 4)
        {
            byte interruptRequest = this.timer.Step(cycles);
            interruptRequest |= this.video.Step(cycles);
            this.serial.Step(cycles);
            this.apu.Step(cycles);
            this.totalCycles += cycles;
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
            bool HaltBug,
            long TotalCycles);

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
