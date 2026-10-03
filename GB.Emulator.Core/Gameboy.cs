#nullable enable
using System;
using System.Collections.Generic;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Core
{
    public class Gameboy
    {
        private readonly Cpu cpu;
        private readonly RomDisassembler disassembler;
        private readonly MemoryMap memory;
        private readonly Video video;
        private readonly Lcd lcd;
        private readonly SpriteTileManager spriteTileManager;
        private readonly BackgroundTileManager backgroundTileManager;
        private readonly Ram cartridgeRam;
        private readonly Ram workRamBank0;
        private readonly Ram workRamBank1;
        private readonly Ram highRam;
        private readonly Ram io;
        private readonly Interrupt interrupt;
        private readonly Apu apu;
        private readonly Timer timer;
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
            this.cartridgeRam = new Ram("RAM0", 0xA000, 0xBFFF);
            this.workRamBank0 = new Ram("RAM1", 0xC000, 0xCFFF);
            this.workRamBank1 = new Ram("RAM2", 0xD000, 0xDFFF);
            this.highRam = new Ram("Internal RAM", 0xFF80, 0xFFFE);
            this.io = new Ram("I/O", 0xFF00, 0xFF4C);
            this.interrupt = new Interrupt();
            this.apu = new Apu();
            this.timer = new Timer();
            this.joypad = new Joypad();
            this.serial = new SerialPort();

            this.memory = new MemoryMap(
                this.joypad,
                this.lcd,
                this.spriteTileManager,
                this.backgroundTileManager,
                this.cartridgeRam,
                this.workRamBank0,
                this.workRamBank1,
                this.highRam,
                this.timer,
                this.apu,
                this.serial,
                this.io,
                this.interrupt);
            this.video = new Video(this.lcd);
            this.cpu = new Cpu(this.memory, this.video, this.serial, this.apu, this.timer);
            this.disassembler = new RomDisassembler(this.cpu);
            this.serial.InterruptRequested += this.OnSerialInterruptRequested;
            this.joypad.InterruptRequested += this.OnJoypadInterruptRequested;
        }

        public Cpu Cpu => this.cpu;

        public MemoryMap Memory => this.memory;

        public Video Video => this.video;

        public VideoState CaptureVideoState() => this.video.CaptureState(this.memory);

        public Apu Sound => this.apu;

        public Timer Timer => this.timer;

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
            this.ResetHardware();
            this.memory.LoadRom(this.romData);
            Cpu.Registers.Reset();
            this.cpu.ResetExecutionState();
        }

        private void ResetHardware()
        {
            this.memory.Reset();
            this.lcd.Reset();
            this.apu.Reset();
            this.timer.Reset();
            this.joypad.Reset();
            this.serial.Reset();
            this.cartridgeRam.Reset();
            this.workRamBank0.Reset();
            this.workRamBank1.Reset();
            this.highRam.Reset();
            this.io.Reset();
            this.interrupt.Reset();
            this.spriteTileManager.Reset();
            this.backgroundTileManager.Reset();
        }

        public void Execute(Cartridge newCartridge)
        {
            this.Load(newCartridge);
            this.ExecuteLoadedRom();
        }

        public void ExecuteLoadedRom()
        {
            this.EnsureCartridgeLoaded("executing");

            this.cpu.Execute(this.romData);
        }

        public CpuStepResult Step()
        {
            this.EnsureCartridgeLoaded("stepping");

            this.memory.UseAccessRecorder(this.debuggerAccesses);
            return this.cpu.ExecuteNextInstruction(this.romData);
        }

        /// <summary>Runs one instruction without collecting debugger access traces.</summary>
        public bool RunStep()
        {
            this.EnsureCartridgeLoaded("running");
            this.memory.UseAccessRecorder(SilentMemoryAccessRecorder.Instance);
            this.cpu.ExecuteNextInstructionFast(this.romData);
            return this.cpu.IsStopped;
        }

        public IReadOnlyList<CpuStepResult> GetInstructionWindow(int instructionsBefore, int instructionsAfter) =>
            this.disassembler.GetInstructionWindow(this.romData, Cpu.Registers.PC, instructionsBefore, instructionsAfter);

        public IReadOnlyList<CpuStepResult> GetDisassembly() =>
            this.disassembler.GetDisassembly(this.romData);

        private void EnsureCartridgeLoaded(string operation)
        {
            if (this.romData.Length == 0)
            {
                throw new InvalidOperationException($"A cartridge must be loaded before {operation}.");
            }
        }

        private void OnSerialInterruptRequested(object? sender, EventArgs e) =>
            this.RequestInterrupt(0x08);

        private void OnJoypadInterruptRequested(object? sender, EventArgs e)
        {
            this.RequestInterrupt(0x10);
            this.cpu.WakeFromStop();
        }

        private void RequestInterrupt(byte interruptMask)
        {
            const ushort interruptRequestAddress = 0xFF0F;
            byte pending = this.memory.Peek(interruptRequestAddress);
            this.memory.Write8((byte)(pending | interruptMask), interruptRequestAddress);
        }

        public GameboyState CaptureState()
        {
            return new GameboyState(
                Cpu.Registers.CaptureState(),
                this.memory.Snapshot(),
                this.cartridgeRam.Snapshot(),
                this.workRamBank0.Snapshot(),
                this.workRamBank1.Snapshot(),
                this.highRam.Snapshot(),
                this.io.Snapshot(),
                this.memory.CaptureCartridgeState(),
                this.timer.Snapshot(),
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

            Cpu.Registers.RestoreState(state.Registers);

            this.memory.RestoreSnapshot(state.Memory);
            this.cartridgeRam.Restore(state.RamBank1);
            this.workRamBank0.Restore(state.RamBank2);
            this.workRamBank1.Restore(state.RamBank3);
            this.highRam.Restore(state.InternalRam);
            this.io.Restore(state.Io);
            this.memory.RestoreCartridgeState(state.CartridgeState);
            this.timer.Restore(state.TimerState);
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
