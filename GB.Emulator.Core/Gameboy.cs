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

            this.memory = new MemoryMap(
                this.lcd,
                this.spriteTileManager,
                this.backgroundTileManager,
                this.ramBank1,
                this.ramBank2,
                this.ramBank3,
                this.internalRam,
                this.io,
                this.interrupt);
            this.video = new Video(this.lcd);
            this.cpu = new Cpu(this.memory, this.video);
        }

        public Cpu Cpu => this.cpu;

        public MemoryMap Memory => this.memory;

        public Cartridge? Cartridge => this.cartridge;

        public void Load(Cartridge newCartridge)
        {
            this.cartridge = newCartridge;
            this.romData = newCartridge.Data;
            this.memory.Reset();
            this.memory.LoadRom(this.romData);
            Cpu.Registers.Reset();
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

            return this.cpu.ExecuteNextInstruction(this.romData);
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
                this.spriteTileManager.Snapshot(),
                this.backgroundTileManager.Snapshot(),
                this.lcd.Snapshot(),
                this.interrupt.Snapshot());
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
            this.spriteTileManager.Restore(state.SpriteTiles);
            this.backgroundTileManager.Restore(state.BackgroundTiles);
            this.lcd.Restore(state.LcdState);
            this.interrupt.Restore(state.InterruptFlags);
        }
    }
}
