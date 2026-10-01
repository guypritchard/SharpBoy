#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class Gameboy
    {
        private readonly Cpu cpu;
        private readonly MemoryMap memory;
        private readonly Video video;
        private readonly Lcd lcd;
        private readonly SpriteTileManager spriteTileManager;
        private readonly BackgroundTileManager backgroundTileManager;
        private readonly Ram ramBank1;
        private readonly Ram ramBank2;
        private readonly Ram ramBank3;
        private readonly Ram internalRam;
        private readonly Ram io;
        private readonly Interrupt interrupt;
        private readonly Apu apu;
        private readonly Joypad joypad;
        private readonly SerialPort serial;
        private readonly RecordingMemoryAccessRecorder debuggerAccesses = new();
        private Cartridge? cartridge;
        private byte[] romData = Array.Empty<byte>();

        public Gameboy()
        {
            this.lcd = new Lcd();
            this.spriteTileManager = new SpriteTileManager();
            this.backgroundTileManager = new BackgroundTileManager();
            this.ramBank1 = new Ram("RAM0", 0xA000, 0xBFFF);
            this.ramBank2 = new Ram("RAM1", 0xC000, 0xCFFF);
            this.ramBank3 = new Ram("RAM2", 0xD000, 0xDFFF);
            this.internalRam = new Ram("Internal RAM", 0xFF80, 0xFFFE);
            this.io = new Ram("I/O", 0xFF00, 0xFF4C);
            this.interrupt = new Interrupt();
            this.apu = new Apu();
            this.joypad = new Joypad();
            this.serial = new SerialPort();

            this.memory = new MemoryMap(
                this.joypad,
                this.lcd,
                this.spriteTileManager,
                this.backgroundTileManager,
                this.ramBank1,
                this.ramBank2,
                this.ramBank3,
                this.internalRam,
                this.apu,
                this.serial,
                this.io,
                this.interrupt);
            this.video = new Video(this.lcd);
            this.cpu = new Cpu(this.memory, this.video, this.serial, this.apu);
            this.serial.InterruptRequested += (_, _) =>
                this.memory.Write8((byte)(this.memory.Peek(0xFF0F) | 0x08), 0xFF0F);
            this.joypad.InterruptRequested += (_, _) =>
            {
                this.memory.Write8((byte)(this.memory.Peek(0xFF0F) | 0x10), 0xFF0F);
                this.cpu.WakeFromStop();
            };
        }

        public Cpu Cpu => this.cpu;

        public MemoryMap Memory => this.memory;

        public Video Video => this.video;

        public VideoState CaptureVideoState() => this.video.CaptureState(this.memory);

        public Apu Sound => this.apu;

        public SoundState CaptureSoundState() => this.apu.CaptureState();

        public IButtonInput Input => this.joypad;

        public SerialPort Serial => this.serial;
        public long EmulatedCycles => this.cpu.TotalCycles;

        public byte Scanline => this.lcd.Scanline;

        public Cartridge? Cartridge => this.cartridge;

        public void Load(Cartridge newCartridge)
        {
            this.cartridge = newCartridge;
            this.romData = newCartridge.Data;
            this.memory.Reset();
            this.lcd.Reset();
            this.apu.Reset();
            this.joypad.Reset();
            this.serial.Reset();
            this.ramBank1.Reset();
            this.ramBank2.Reset();
            this.ramBank3.Reset();
            this.internalRam.Reset();
            this.io.Reset();
            this.interrupt.Reset();
            this.spriteTileManager.Reset();
            this.backgroundTileManager.Reset();
            this.memory.LoadRom(this.romData);
            Cpu.Registers.Reset();
            this.cpu.ResetExecutionState();
        }

        public void Execute(Cartridge newCartridge)
        {
            this.Load(newCartridge);
            this.ExecuteLoadedRom();
        }

        public void ExecuteLoadedRom()
        {
            if (this.romData.Length == 0)
            {
                throw new InvalidOperationException("A cartridge must be loaded before executing.");
            }

            this.cpu.Execute(this.romData);
        }

        public CpuStepResult Step()
        {
            if (this.romData.Length == 0)
            {
                throw new InvalidOperationException("A cartridge must be loaded before stepping.");
            }

            this.memory.UseAccessRecorder(this.debuggerAccesses);
            return this.cpu.ExecuteNextInstruction(this.romData);
        }

        /// <summary>Runs one instruction without collecting debugger access traces.</summary>
        public bool RunStep()
        {
            if (this.romData.Length == 0)
                throw new InvalidOperationException("A cartridge must be loaded before running.");
            this.memory.UseAccessRecorder(SilentMemoryAccessRecorder.Instance);
            this.cpu.ExecuteNextInstructionFast(this.romData);
            return this.cpu.IsStopped;
        }

        public IReadOnlyList<CpuStepResult> GetInstructionWindow(int instructionsBefore, int instructionsAfter)
        {
            if (this.romData.Length == 0)
            {
                return Array.Empty<CpuStepResult>();
            }

            var queue = new Queue<CpuStepResult>();
            int address = 0;
            CpuStepResult? current = null;

            while (address < this.romData.Length)
            {
                CpuStepResult decoded = this.cpu.DecodeInstruction(this.romData, (ushort)address);
                queue.Enqueue(decoded);
                if (queue.Count > instructionsBefore + 1)
                {
                    queue.Dequeue();
                }

                int length = decoded.Instruction.Length;
                if (length <= 0)
                {
                    length = 1;
                }

                address += length;

                if (decoded.Address == Cpu.Registers.PC)
                {
                    current = decoded;
                    break;
                }
            }

            if (current == null)
            {
                return Array.Empty<CpuStepResult>();
            }

            var window = queue.ToList();

            int nextLength = current.Instruction.Length;
            if (nextLength <= 0)
            {
                nextLength = 1;
            }

            int nextAddress = current.Address + nextLength;

            for (int i = 0; i < instructionsAfter && nextAddress < this.romData.Length; i++)
            {
                CpuStepResult next = this.cpu.DecodeInstruction(this.romData, (ushort)nextAddress);
                window.Add(next);
                int length = next.Instruction.Length;
                if (length <= 0)
                {
                    length = 1;
                }

                nextAddress += length;
            }

            return window;
        }

        public IReadOnlyList<CpuStepResult> GetDisassembly()
        {
            if (this.romData.Length == 0)
            {
                return Array.Empty<CpuStepResult>();
            }

            var results = new List<CpuStepResult>();
            int address = 0;

            while (address < this.romData.Length)
            {
                CpuStepResult decoded;
                try
                {
                    decoded = this.cpu.DecodeInstruction(this.romData, (ushort)address);
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }

                results.Add(decoded);

                int length = decoded.Instruction.Length;
                if (length <= 0)
                {
                    length = 1;
                }

                address += length;
            }

            return results;
        }

        public GameboyState CaptureState()
        {
            var registers = new CpuRegistersState(
                Cpu.Registers.A,
                Cpu.Registers.F,
                Cpu.Registers.B,
                Cpu.Registers.C,
                Cpu.Registers.D,
                Cpu.Registers.E,
                Cpu.Registers.H,
                Cpu.Registers.L,
                Cpu.Registers.Flags,
                Cpu.Registers.SP,
                Cpu.Registers.PC);

            return new GameboyState(
                registers,
                this.memory.Snapshot(),
                this.ramBank1.Snapshot(),
                this.ramBank2.Snapshot(),
                this.ramBank3.Snapshot(),
                this.internalRam.Snapshot(),
                this.io.Snapshot(),
                this.apu.Snapshot(),
                this.joypad.Snapshot(),
                this.serial.Snapshot(),
                this.spriteTileManager.Snapshot(),
                this.backgroundTileManager.Snapshot(),
                this.lcd.Snapshot(),
                this.interrupt.Snapshot(),
                this.cpu.CaptureExecutionState());
        }

        public void RestoreState(GameboyState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            Cpu.Registers.A = state.Registers.A;
            Cpu.Registers.F = state.Registers.F;
            Cpu.Registers.B = state.Registers.B;
            Cpu.Registers.C = state.Registers.C;
            Cpu.Registers.D = state.Registers.D;
            Cpu.Registers.E = state.Registers.E;
            Cpu.Registers.H = state.Registers.H;
            Cpu.Registers.L = state.Registers.L;
            Cpu.Registers.Flags = state.Registers.Flags;
            Cpu.Registers.SP = state.Registers.SP;
            Cpu.Registers.PC = state.Registers.PC;

            this.memory.RestoreSnapshot(state.Memory);
            this.ramBank1.Restore(state.RamBank1);
            this.ramBank2.Restore(state.RamBank2);
            this.ramBank3.Restore(state.RamBank3);
            this.internalRam.Restore(state.InternalRam);
            this.io.Restore(state.Io);
            this.apu.Restore(state.SoundState);
            this.joypad.Restore(state.JoypadState);
            this.serial.Restore(state.SerialState);
            this.spriteTileManager.Restore(state.SpriteTiles);
            this.backgroundTileManager.Restore(state.BackgroundTiles);
            this.lcd.Restore(state.LcdState);
            this.interrupt.Restore(state.InterruptFlags);
            this.cpu.RestoreExecutionState(state.CpuExecutionState);
        }
    }
}
