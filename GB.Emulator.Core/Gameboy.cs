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
            var lcd = new Lcd();
            this.spriteTileManager = new SpriteTileManager();
            this.backgroundTileManager = new BackgroundTileManager();
            this.ramBank1 = new Ram("RAM0", 0xA000, 0xBFFF);
            this.ramBank2 = new Ram("RAM1", 0xC000, 0xCFFF);
            this.ramBank3 = new Ram("RAM2", 0xD000, 0xDFFF);
            this.internalRam = new Ram("Internal RAM", 0xFF80, 0xFFFE);
            this.io = new Ram("I/O", 0xFF00, 0xFF4C);
            this.interrupt = new Interrupt();

            this.memory = new MemoryMap(
                lcd,
                this.spriteTileManager,
                this.backgroundTileManager,
                this.ramBank1,
                this.ramBank2,
                this.ramBank3,
                this.internalRam,
                this.io,
                this.interrupt);
            this.video = new Video(lcd);
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
    }
}
