using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CpuInstructionTests
    {
        private static byte[] CreateRom(params byte[] bytes)
        {
            // Place the instruction at the reset PC (0x0100).
            var rom = new byte[0x200];
            for (int i = 0; i < bytes.Length; i++)
            {
                rom[0x0100 + i] = bytes[i];
            }
            return rom;
        }

        private static Gameboy CreateGameboy(params byte[] romBytes)
        {
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = CreateRom(romBytes) });
            return gameboy;
        }

        [TestInitialize]
        public void ResetCpu()
        {
            Cpu.Registers.Reset();
        }

        [TestMethod]
        public void LdA8A_WritesToHighRam()
        {
            byte operand = 0x12;
            byte value = 0x5A;
            var gameboy = CreateGameboy(0xE0, operand);
            Cpu.Registers.A = value;

            gameboy.Step();

            Assert.AreEqual(value, gameboy.Memory.Peek((ushort)(0xFF00 + operand)));
        }

        [TestMethod]
        public void LdA8A_HandlesTopOfHighRam()
        {
            byte operand = 0xFF;
            byte value = 0xA5;
            var gameboy = CreateGameboy(0xE0, operand);
            Cpu.Registers.A = value;

            gameboy.Step();

            Assert.AreEqual(value, gameboy.Memory.Peek(0xFFFF));
        }

        [TestMethod]
        public void Nop_AdvancesProgramCounter()
        {
            var gameboy = CreateGameboy(0x00);

            gameboy.Step();

            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void Di_AdvancesProgramCounter()
        {
            var gameboy = CreateGameboy(0xF3);

            gameboy.Step();

            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdBB_NoOp()
        {
            var gameboy = CreateGameboy(0x40);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.B = 0x12;

            gameboy.Step();

            Assert.AreEqual(0x12, Cpu.Registers.B);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdCC_NoOp()
        {
            var gameboy = CreateGameboy(0x49);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.C = 0x34;

            gameboy.Step();

            Assert.AreEqual(0x34, Cpu.Registers.C);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdDD_NoOp()
        {
            var gameboy = CreateGameboy(0x52);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.D = 0x56;

            gameboy.Step();

            Assert.AreEqual(0x56, Cpu.Registers.D);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdEE_NoOp()
        {
            var gameboy = CreateGameboy(0x5B);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.E = 0x78;

            gameboy.Step();

            Assert.AreEqual(0x78, Cpu.Registers.E);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdHH_NoOp()
        {
            var gameboy = CreateGameboy(0x64);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.H = 0x9A;

            gameboy.Step();

            Assert.AreEqual(0x9A, Cpu.Registers.H);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdLL_NoOp()
        {
            var gameboy = CreateGameboy(0x6D);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.L = 0xBC;

            gameboy.Step();

            Assert.AreEqual(0xBC, Cpu.Registers.L);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdAA_NoOp()
        {
            var gameboy = CreateGameboy(0x7F);
            Cpu.Registers.Flags = 0xF0;
            Cpu.Registers.A = 0xDE;

            gameboy.Step();

            Assert.AreEqual(0xDE, Cpu.Registers.A);
            Assert.AreEqual(0xF0, Cpu.Registers.Flags);
            Assert.AreEqual(0x0101, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdBcA_WritesToMemory()
        {
            var gameboy = CreateGameboy(0x02);
            Cpu.Registers.BC = 0xC123;
            Cpu.Registers.A = 0x42;

            gameboy.Step();

            Assert.AreEqual(0x42, gameboy.Memory.Peek(0xC123));
        }

        [TestMethod]
        public void LdHlImmediate_SetsRegisterPair()
        {
            var gameboy = CreateGameboy(0x21, 0x34, 0x12);

            gameboy.Step();

            Assert.AreEqual(0x1234, Cpu.Registers.HL);
            Assert.AreEqual(0x0103, Cpu.Registers.PC);
        }

        [TestMethod]
        public void LdHlPlusA_WritesAndIncrementsHl()
        {
            var gameboy = CreateGameboy(0x22);
            Cpu.Registers.HL = 0xC010;
            Cpu.Registers.A = 0x77;

            gameboy.Step();

            Assert.AreEqual(0x77, gameboy.Memory.Peek(0xC010));
            Assert.AreEqual(0xC011, Cpu.Registers.HL);
        }

        [TestMethod]
        public void LdAFromHlPlus_LoadsLowByteAndIncrementsHl()
        {
            var gameboy = CreateGameboy(0x2A);
            Cpu.Registers.HL = 0xC020;
            gameboy.Memory.Write8(0xAB, 0xC020);
            gameboy.Memory.Write8(0xCD, 0xC021);

            gameboy.Step();

            Assert.AreEqual(0xAB, Cpu.Registers.A);
            Assert.AreEqual(0xC021, Cpu.Registers.HL);
        }

        [TestMethod]
        public void LdSpImmediate_SetsStackPointer()
        {
            var gameboy = CreateGameboy(0x31, 0x34, 0x12);

            gameboy.Step();

            Assert.AreEqual(0x1234, Cpu.Registers.SP);
        }

        [TestMethod]
        public void LdHlMinusA_WritesAndDecrementsHl()
        {
            var gameboy = CreateGameboy(0x32);
            Cpu.Registers.HL = 0xC030;
            Cpu.Registers.A = 0x99;

            gameboy.Step();

            Assert.AreEqual(0x99, gameboy.Memory.Peek(0xC030));
            Assert.AreEqual(0xC02F, Cpu.Registers.HL);
        }

        [TestMethod]
        public void LdAImmediate_SetsA()
        {
            var gameboy = CreateGameboy(0x3E, 0x5A);

            gameboy.Step();

            Assert.AreEqual(0x5A, Cpu.Registers.A);
        }

        [TestMethod]
        public void LdBFromE_CopiesRegister()
        {
            var gameboy = CreateGameboy(0x43);
            Cpu.Registers.E = 0x11;

            gameboy.Step();

            Assert.AreEqual(0x11, Cpu.Registers.B);
        }

        [TestMethod]
        public void LdEFromA_CopiesRegister()
        {
            var gameboy = CreateGameboy(0x5F);
            Cpu.Registers.A = 0x22;

            gameboy.Step();

            Assert.AreEqual(0x22, Cpu.Registers.E);
        }

        [TestMethod]
        public void LdHFromHl_ReadsMemory()
        {
            var gameboy = CreateGameboy(0x66);
            Cpu.Registers.HL = 0xC040;
            gameboy.Memory.Write8(0x33, 0xC040);

            gameboy.Step();

            Assert.AreEqual(0x33, Cpu.Registers.H);
        }

        [TestMethod]
        public void LdLFromHl_ReadsMemory()
        {
            var gameboy = CreateGameboy(0x67);
            Cpu.Registers.HL = 0xC041;
            gameboy.Memory.Write8(0x44, 0xC041);

            gameboy.Step();

            Assert.AreEqual(0x44, Cpu.Registers.L);
        }

        [TestMethod]
        public void LdAFromHl_ReadsMemory()
        {
            var gameboy = CreateGameboy(0x7E);
            Cpu.Registers.HL = 0xC050;
            gameboy.Memory.Write8(0x55, 0xC050);

            gameboy.Step();

            Assert.AreEqual(0x55, Cpu.Registers.A);
        }

        [TestMethod]
        public void LdAFromA_UsesMemoryAddressedByA()
        {
            var gameboy = CreateGameboy(0x7F);
            Cpu.Registers.A = 0x42;
            gameboy.Memory.Write8(0x99, 0x0042);

            gameboy.Step();

            Assert.AreEqual(0x42, Cpu.Registers.A);
        }

        [TestMethod]
        public void LdCA_WritesToHighRamOffsetByC()
        {
            var gameboy = CreateGameboy(0xE2);
            Cpu.Registers.C = 0x20;
            Cpu.Registers.A = 0xAB;

            gameboy.Step();

            Assert.AreEqual(0xAB, gameboy.Memory.Peek(0xFF20));
        }

        [TestMethod]
        public void LdA16A_WritesToAbsoluteAddress()
        {
            var gameboy = CreateGameboy(0xEA, 0x23, 0xC1);
            Cpu.Registers.A = 0xBE;

            gameboy.Step();

            Assert.AreEqual(0xBE, gameboy.Memory.Peek(0xC123));
        }

        [TestMethod]
        public void LdA8_ReadsFromHighRam()
        {
            var gameboy = CreateGameboy(0xF0, 0x10);
            gameboy.Memory.Write8(0x7F, 0xFF10);

            gameboy.Step();

            Assert.AreEqual(0x7F, Cpu.Registers.A);
        }

        [TestMethod]
        public void AddHlSp_AddsRegisterPairs()
        {
            var gameboy = CreateGameboy(0x39);
            Cpu.Registers.HL = 0x0001;
            Cpu.Registers.SP = 0x0002;

            gameboy.Step();

            Assert.AreEqual(0x0003, Cpu.Registers.HL);
        }

        [TestMethod]
        public void AddAA_UpdatesFlagsAndAccumulator()
        {
            var gameboy = CreateGameboy(0x87);
            Cpu.Registers.A = 0x01;

            gameboy.Step();

            Assert.AreEqual(0x02, Cpu.Registers.A);
            Assert.AreEqual(0x00, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void AddSpSignedByte_AddsSignedOperand()
        {
            var gameboy = CreateGameboy(0xE8, 0xFE);
            Cpu.Registers.SP = 0x0100;

            gameboy.Step();

            Assert.AreEqual(0x00FE, Cpu.Registers.SP);
        }

        [TestMethod]
        public void SubAL_UpdatesFlags()
        {
            var gameboy = CreateGameboy(0x95);
            Cpu.Registers.A = 0x10;
            Cpu.Registers.L = 0x01;

            gameboy.Step();

            Assert.AreEqual(0x0F, Cpu.Registers.A);
            Assert.AreEqual(0x60, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void SubAA_SetsZeroAndSubtractFlags()
        {
            var gameboy = CreateGameboy(0x97);
            Cpu.Registers.A = 0x01;

            gameboy.Step();

            Assert.AreEqual(0x00, Cpu.Registers.A);
            Assert.AreEqual(0xC0, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void SbcAH_ConsumesCarry()
        {
            var gameboy = CreateGameboy(0x9C);
            Cpu.Registers.A = 0x10;
            Cpu.Registers.H = 0x01;
            Cpu.Flags.C = true;

            gameboy.Step();

            Assert.AreEqual(0x0E, Cpu.Registers.A);
            Assert.AreEqual(0x60, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void XorA_ClearsAccumulator()
        {
            var gameboy = CreateGameboy(0xAF);
            Cpu.Registers.A = 0x5A;

            gameboy.Step();

            Assert.AreEqual(0x00, Cpu.Registers.A);
        }

        [TestMethod]
        public void OrB_UpdatesAccumulatorAndFlags()
        {
            var gameboy = CreateGameboy(0xB0);
            Cpu.Registers.A = 0xF0;
            Cpu.Registers.B = 0x0F;

            gameboy.Step();

            Assert.AreEqual(0xFF, Cpu.Registers.A);
            Assert.AreEqual(0x00, Cpu.Registers.Flags);
        }

        [TestMethod]
        public void OrFromHl_UpdatesAccumulator()
        {
            var gameboy = CreateGameboy(0xB6);
            Cpu.Registers.A = 0x0F;
            Cpu.Registers.HL = 0xC060;
            gameboy.Memory.Write8(0xF0, 0xC060);

            gameboy.Step();

            Assert.AreEqual(0xFF, Cpu.Registers.A);
        }

        [TestMethod]
        public void CpImmediate_SetsZeroWhenEqual()
        {
            var gameboy = CreateGameboy(0xFE, 0x77);
            Cpu.Registers.A = 0x77;

            gameboy.Step();

            Assert.IsTrue(Cpu.Flags.Z);
        }

        [TestMethod]
        public void IncDec_IncrementsAndDecrements()
        {
            var gameboy = CreateGameboy(0x03, 0x05);
            Cpu.Registers.BC = 0x1000;

            gameboy.Step();
            Assert.AreEqual(0x1001, Cpu.Registers.BC);

            Cpu.Registers.B = 0x01;
            gameboy.Step();
            Assert.AreEqual(0x00, Cpu.Registers.B);
            Assert.IsTrue(Cpu.Flags.Z);
        }

        [TestMethod]
        public void JrNz_TakenAdjustsPc()
        {
            var gameboy = CreateGameboy(0x20, 0x02);
            Cpu.Flags.Z = false;

            gameboy.Step();

            Assert.AreEqual(0x0104, Cpu.Registers.PC);
        }

        [TestMethod]
        public void JrNz_NotTakenAdvancesByLength()
        {
            var gameboy = CreateGameboy(0x20, 0x02);
            Cpu.Flags.Z = true;

            gameboy.Step();

            Assert.AreEqual(0x0102, Cpu.Registers.PC);
        }

        [TestMethod]
        public void JrNc_TakenAdjustsPc()
        {
            var gameboy = CreateGameboy(0x30, 0x02);
            Cpu.Flags.C = false;

            gameboy.Step();

            Assert.AreEqual(0x0104, Cpu.Registers.PC);
        }

        [TestMethod]
        public void JpAbsolute_SetsProgramCounter()
        {
            var gameboy = CreateGameboy(0xC3, 0x34, 0x12);

            gameboy.Step();

            Assert.AreEqual(0x1234, Cpu.Registers.PC);
        }

        [TestMethod]
        public void RetNc_TakenPopsAddress()
        {
            var gameboy = CreateGameboy(0xD0);
            Cpu.Registers.SP = 0xFFFC;
            gameboy.Memory.Write8(0x34, 0xFFFC);
            gameboy.Memory.Write8(0x12, 0xFFFD);
            Cpu.Flags.C = false;

            gameboy.Step();

            Assert.AreEqual(0x1234, Cpu.Registers.PC);
            Assert.AreEqual(0xFFFE, Cpu.Registers.SP);
        }

        [TestMethod]
        public void RetC_TakenPopsAddress()
        {
            var gameboy = CreateGameboy(0xD8);
            Cpu.Registers.SP = 0xFFFC;
            gameboy.Memory.Write8(0x78, 0xFFFC);
            gameboy.Memory.Write8(0x56, 0xFFFD);
            Cpu.Flags.C = true;

            gameboy.Step();

            Assert.AreEqual(0x5678, Cpu.Registers.PC);
            Assert.AreEqual(0xFFFE, Cpu.Registers.SP);
        }

        [TestMethod]
        public void Call_PushesReturnAddressAndJumps()
        {
            var gameboy = CreateGameboy(0xCD, 0x00, 0x20);
            Cpu.Registers.SP = 0xFFFE;

            gameboy.Step();

            Assert.AreEqual(0x2000, Cpu.Registers.PC);
            Assert.AreEqual(0xFFFC, Cpu.Registers.SP);
            Assert.AreEqual(0x03, gameboy.Memory.Peek(0xFFFC));
            Assert.AreEqual(0x01, gameboy.Memory.Peek(0xFFFD));
        }

        [TestMethod]
        public void PopDe_ReadsFromStack()
        {
            var gameboy = CreateGameboy(0xD1);
            Cpu.Registers.SP = 0xFFFC;
            gameboy.Memory.Write8(0x34, 0xFFFC);
            gameboy.Memory.Write8(0x12, 0xFFFD);

            gameboy.Step();

            Assert.AreEqual(0x12, Cpu.Registers.D);
            Assert.AreEqual(0x34, Cpu.Registers.E);
            Assert.AreEqual(0xFFFE, Cpu.Registers.SP);
        }

        [TestMethod]
        public void PushHl_WritesToStack()
        {
            var gameboy = CreateGameboy(0xE5);
            Cpu.Registers.HL = 0x1234;
            Cpu.Registers.SP = 0xFFFE;

            gameboy.Step();

            Assert.AreEqual(0xFFFC, Cpu.Registers.SP);
            Assert.AreEqual(0x34, gameboy.Memory.Peek(0xFFFC));
            Assert.AreEqual(0x12, gameboy.Memory.Peek(0xFFFD));
        }

        [TestMethod]
        public void PushAf_WritesToStack()
        {
            var gameboy = CreateGameboy(0xF5);
            Cpu.Registers.A = 0xAB;
            Cpu.Registers.F = 0xCD;
            Cpu.Registers.SP = 0xFFFE;

            gameboy.Step();

            Assert.AreEqual(0xFFFC, Cpu.Registers.SP);
            Assert.AreEqual(0xCD, gameboy.Memory.Peek(0xFFFC));
            Assert.AreEqual(0xAB, gameboy.Memory.Peek(0xFFFD));
        }
    }
}
