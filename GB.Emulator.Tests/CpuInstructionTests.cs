using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CpuInstructionTests
    {
        private static byte[] CreateRom(byte opcode, byte operand)
        {
            // Place the instruction at the reset PC (0x0100).
            var rom = new byte[0x200];
            rom[0x0100] = opcode;
            rom[0x0101] = operand;
            return rom;
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
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = CreateRom(0xE0, operand) });
            Cpu.Registers.A = value;

            gameboy.Step();

            Assert.AreEqual(value, gameboy.Memory.Peek((ushort)(0xFF00 + operand)));
        }

        [TestMethod]
        public void LdA8A_HandlesTopOfHighRam()
        {
            byte operand = 0xFF;
            byte value = 0xA5;
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = CreateRom(0xE0, operand) });
            Cpu.Registers.A = value;

            gameboy.Step();

            Assert.AreEqual(value, gameboy.Memory.Peek(0xFFFF));
        }
    }
}
