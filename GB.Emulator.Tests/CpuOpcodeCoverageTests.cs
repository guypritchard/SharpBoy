using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CpuOpcodeCoverageTests
    {
        private static Gameboy CreateGameboy(params byte[] instruction)
        {
            var rom = new byte[0x200];
            Array.Copy(instruction, 0, rom, 0x100, instruction.Length);
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = rom });
            return gameboy;
        }

        [TestMethod]
        public void EveryDocumentedBaseOpcodeExecutes()
        {
            int[] illegal = { 0xD3, 0xDB, 0xDD, 0xE3, 0xE4, 0xEB, 0xEC, 0xED, 0xF4, 0xFC, 0xFD };
            for (int opcode = 0; opcode <= 0xFF; opcode++)
            {
                if (Array.IndexOf(illegal, opcode) >= 0) continue;
                var gameboy = CreateGameboy((byte)opcode, 0, 0);
                try
                {
                    CpuStepResult step = gameboy.Step();
                    Assert.AreEqual(opcode, step.Instruction.Value, $"Opcode 0x{opcode:X2}");
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Opcode 0x{opcode:X2} failed: {ex}");
                }
            }
        }

        [TestMethod]
        public void EveryCbOpcodeExecutesAndDecodes()
        {
            for (int opcode = 0; opcode <= 0xFF; opcode++)
            {
                var gameboy = CreateGameboy(0xCB, (byte)opcode);
                CpuStepResult step = gameboy.Step();
                Assert.AreEqual(0x102, Cpu.Registers.PC, $"CB 0x{opcode:X2}");
                Assert.AreNotEqual("PREFIX CB", step.Instruction.Name, $"CB 0x{opcode:X2}");
            }
        }

        [TestMethod]
        public void UndefinedOpcodeIsRejected()
        {
            var gameboy = CreateGameboy(0xD3);
            Assert.ThrowsExactly<NotImplementedException>(() => gameboy.Step());
        }

        [TestMethod]
        public void LoadPairAndRegisterMatrixUseHardwareOpcodes()
        {
            var gameboy = CreateGameboy(0x01, 0x34, 0x12, 0x12);
            gameboy.Step();
            Assert.AreEqual(0x1234, Cpu.Registers.BC);
            Assert.AreEqual(0x103, Cpu.Registers.PC);

            Cpu.Registers.DE = 0xC000;
            Cpu.Registers.A = 0x5A;
            gameboy.Step();
            Assert.AreEqual(0x5A, gameboy.Memory.Peek(0xC000));
        }

        [TestMethod]
        public void ArithmeticAndCompareSetAllFlags()
        {
            var gameboy = CreateGameboy(0xDE, 0x01, 0xFE, 0xFF);
            Cpu.Registers.A = 0;
            Cpu.Flags.C = true;
            gameboy.Step();
            Assert.AreEqual(0xFE, Cpu.Registers.A);
            Assert.AreEqual(0x70, Cpu.Registers.Flags);

            gameboy.Step();
            Assert.AreEqual(0xFE, Cpu.Registers.A);
            Assert.AreEqual(0x70, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void ConditionalCallAndReturnPreserveStackAndPc()
        {
            var gameboy = CreateGameboy(0xC4, 0x20, 0x01);
            Cpu.Registers.SP = 0xFFFE;
            Cpu.Flags.Z = false;
            gameboy.Step();
            Assert.AreEqual(0x120, Cpu.Registers.PC);
            Assert.AreEqual(0xFFFC, Cpu.Registers.SP);
            Assert.AreEqual(0x0103, gameboy.Memory.Read16(0xFFFC));
        }

        [TestMethod]
        public void CbBitResAndSetPreserveExpectedFlags()
        {
            var gameboy = CreateGameboy(0xCB, 0x7C, 0xCB, 0xBC, 0xCB, 0xFC);
            Cpu.Registers.H = 0x00;
            Cpu.Flags.C = true;
            gameboy.Step();
            Assert.AreEqual(0xB0, Cpu.Registers.Flags);
            gameboy.Step();
            Assert.AreEqual(0x00, Cpu.Registers.H);
            Assert.AreEqual(0xB0, Cpu.Registers.Flags);
            gameboy.Step();
            Assert.AreEqual(0x80, Cpu.Registers.H);
            Assert.AreEqual(0xB0, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void PopAfMasksUnusedFlagBits()
        {
            var gameboy = CreateGameboy(0xF1);
            Cpu.Registers.SP = 0xFFFC;
            gameboy.Memory.Write16(0x12FF, 0xFFFC);
            gameboy.Step();
            Assert.AreEqual(0x12, Cpu.Registers.A);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void EiEnablesInterruptsAfterFollowingInstruction()
        {
            var gameboy = CreateGameboy(0xFB, 0x00, 0x00);
            gameboy.Memory.Write8(0x01, 0xFFFF);
            gameboy.Memory.Write8(0x01, 0xFF0F);

            gameboy.Step();
            Assert.AreEqual(0x101, Cpu.Registers.PC);
            gameboy.Step();
            Assert.AreEqual(0x102, Cpu.Registers.PC);
            gameboy.Step();
            Assert.AreEqual(0x40, Cpu.Registers.PC);
            Assert.AreEqual(0x0102, gameboy.Memory.Read16(Cpu.Registers.SP));
        }

        [TestMethod]
        public void ConsecutiveEiDoesNotDelayFirstEnableAgain()
        {
            var gameboy = CreateGameboy(0xFB, 0xFB, 0x00);
            gameboy.Memory.Write8(1, 0xFFFF);
            gameboy.Memory.Write8(1, 0xFF0F);
            gameboy.Step();
            gameboy.Step();
            Assert.AreEqual(0x102, Cpu.Registers.PC);
            gameboy.Step();
            Assert.AreEqual(0x40, Cpu.Registers.PC);
        }

        [TestMethod]
        public void HaltAndStopDoNotTerminateProcess()
        {
            var halt = CreateGameboy(0x76, 0x00);
            halt.Step();
            Assert.AreEqual(0x101, Cpu.Registers.PC);
            halt.Step();
            Assert.AreEqual(0x101, Cpu.Registers.PC);

            var stop = CreateGameboy(0x10, 0x00, 0x00);
            stop.Step();
            Assert.AreEqual(0x102, Cpu.Registers.PC);
            stop.Step();
            Assert.AreEqual(0x102, Cpu.Registers.PC);
        }

        [TestMethod]
        public void HaltBugRepeatsNextOpcodeWhenInterruptIsPendingWithImeOff()
        {
            var gameboy = CreateGameboy(0x76, 0x04, 0x00);
            gameboy.Memory.Write8(1, 0xFFFF);
            gameboy.Memory.Write8(1, 0xFF0F);
            gameboy.Step();
            gameboy.Step();
            Assert.AreEqual(1, Cpu.Registers.B);
            Assert.AreEqual(0x101, Cpu.Registers.PC);
            gameboy.Step();
            Assert.AreEqual(2, Cpu.Registers.B);
            Assert.AreEqual(0x102, Cpu.Registers.PC);
        }

        [TestMethod]
        public void DaaConvertsBinarySumToBcd()
        {
            var gameboy = CreateGameboy(0xC6, 0x27, 0x27);
            Cpu.Registers.A = 0x15;
            gameboy.Step();
            gameboy.Step();
            Assert.AreEqual(0x42, Cpu.Registers.A);
            Assert.AreEqual(0x00, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void SignedSpAdditionSetsHalfCarryAndCarry()
        {
            var gameboy = CreateGameboy(0xE8, 0x01);
            Cpu.Registers.SP = 0x00FF;
            gameboy.Step();
            Assert.AreEqual(0x0100, Cpu.Registers.SP);
            Assert.AreEqual(0x30, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void NegativeSpOffsetUsesSignedCarryRules()
        {
            var gameboy = CreateGameboy(0xF8, 0xFE);
            Cpu.Registers.SP = 0x0100;
            gameboy.Step();
            Assert.AreEqual(0x00FE, Cpu.Registers.HL);
            Assert.AreEqual(0x00, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void InstructionCyclesAdvanceLcdTiming()
        {
            var instructions = new byte[38 * 3];
            for (int i = 0; i < instructions.Length; i += 3)
            {
                instructions[i] = 0x01; // LD BC, d16 takes 12 cycles.
            }

            var gameboy = CreateGameboy(instructions);
            gameboy.Memory.Write8(0x80, 0xFF40);
            for (int i = 0; i < 38; i++) gameboy.Step();

            Assert.AreEqual(1, gameboy.Scanline);
        }
    }
}
