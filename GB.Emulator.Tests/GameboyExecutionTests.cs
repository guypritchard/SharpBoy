using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class GameboyExecutionTests
    {
        [TestMethod]
        public void InstructionWindowFollowsVariableLengthInstructionsWithoutExecutingThem()
        {
            var gameboy = CreateGameboy(0x00, 0x3E, 0x42, 0x21, 0x34, 0x12, 0xCB, 0x11, 0x00);
            Cpu.Registers.PC = 0x0103;
            Cpu.Registers.A = 0x77;

            var window = gameboy.GetInstructionWindow(2, 2);

            CollectionAssert.AreEqual(new ushort[] { 0x0100, 0x0101, 0x0103, 0x0106, 0x0108 },
                window.Select(instruction => instruction.Address).ToArray());
            Assert.AreEqual(0x34, window[2].Operand1);
            Assert.AreEqual(0x12, window[2].Operand2);
            Assert.AreEqual("RL C", window[3].Instruction.Name);
            Assert.AreEqual(0x0103, Cpu.Registers.PC);
            Assert.AreEqual(0x77, Cpu.Registers.A);
            Assert.AreEqual(0L, gameboy.EmulatedCycles);
        }

        [TestMethod]
        public void InstructionWindowIsEmptyWhenProgramCounterIsInsideAnOperand()
        {
            var gameboy = CreateGameboy(0x21, 0x34, 0x12);
            Cpu.Registers.PC = 0x0101;

            Assert.IsEmpty(gameboy.GetInstructionWindow(2, 2));
        }

        [TestMethod]
        public void DisassemblyStopsBeforeAnIncompleteInstruction()
        {
            var rom = new byte[0x0103];
            rom[0x0100] = 0x3E;
            rom[0x0101] = 0x42;
            rom[0x0102] = 0x21; // LD HL,d16 has no operand bytes at the end of this ROM.
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = rom });

            var disassembly = gameboy.GetDisassembly();

            Assert.HasCount(0x0101, disassembly);
            Assert.AreEqual(0x0100, disassembly.Last().Address);
            Assert.AreEqual(0x42, disassembly.Last().Operand1);
        }

        [TestMethod]
        public void UnloadedGameboyHasNoDisassemblyAndRejectsExecution()
        {
            var gameboy = new Gameboy();

            Assert.IsEmpty(gameboy.GetDisassembly());
            Assert.IsEmpty(gameboy.GetInstructionWindow(2, 2));
            Assert.Throws<InvalidOperationException>(() => gameboy.Step());
            Assert.Throws<InvalidOperationException>(() => gameboy.RunStep());
            Assert.Throws<InvalidOperationException>(() => gameboy.ExecuteLoadedRom());
        }

        [TestMethod]
        public void RestoringStateReplaysAnInstructionWithTheSameRegistersMemoryAndTiming()
        {
            var gameboy = CreateGameboy(0x22); // LD (HL+),A
            Cpu.Registers.A = 0x5A;
            Cpu.Registers.BC = 0x1234;
            Cpu.Registers.DE = 0x5678;
            Cpu.Registers.HL = 0xC000;
            Cpu.Registers.F = 0xB0;
            Cpu.Registers.SP = 0xFFFC;
            GameboyState saved = gameboy.CaptureState();

            CpuStepResult first = gameboy.Step();
            long cycles = gameboy.EmulatedCycles;
            Cpu.Registers.A = 0;
            Cpu.Registers.BC = 0;
            Cpu.Registers.DE = 0;
            Cpu.Registers.F = 0;
            Cpu.Registers.SP = 0;
            gameboy.RestoreState(saved);

            Assert.AreEqual(0x5A, Cpu.Registers.A);
            Assert.AreEqual(0x1234, Cpu.Registers.BC);
            Assert.AreEqual(0x5678, Cpu.Registers.DE);
            Assert.AreEqual(0xC000, Cpu.Registers.HL);
            Assert.AreEqual(0xB0, Cpu.Registers.F);
            Assert.AreEqual(0xFFFC, Cpu.Registers.SP);
            Assert.AreEqual(0x0100, Cpu.Registers.PC);
            Assert.AreEqual(0, gameboy.Memory.Read8(0xC000));
            Assert.AreEqual(0L, gameboy.EmulatedCycles);

            CpuStepResult replayed = gameboy.Step();

            Assert.AreEqual(first.Address, replayed.Address);
            Assert.AreEqual(first.Disassembly, replayed.Disassembly);
            CollectionAssert.AreEquivalent(first.WrittenAddresses.ToArray(), replayed.WrittenAddresses.ToArray());
            Assert.AreEqual(0x5A, gameboy.Memory.Read8(0xC000));
            Assert.AreEqual(0xC001, Cpu.Registers.HL);
            Assert.AreEqual(cycles, gameboy.EmulatedCycles);
        }

        private static Gameboy CreateGameboy(params byte[] instructions)
        {
            var rom = new byte[0x0200];
            Array.Copy(instructions, 0, rom, 0x0100, instructions.Length);
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = rom });
            return gameboy;
        }
    }
}
