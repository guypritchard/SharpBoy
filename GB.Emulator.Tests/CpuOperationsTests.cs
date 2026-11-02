using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CpuOperationsTests
    {
        private Gameboy gameboy = new();
        [TestInitialize]
        public void ResetCpuState()
        {
            this.gameboy.Cpu.Registers.Reset();
        }

        private void AssertFlags(byte expected)
        {
            Assert.AreEqual(expected, this.gameboy.Cpu.Registers.Flags, $"Flags: 0x{this.gameboy.Cpu.Registers.Flags:X2}");
        }

        [TestMethod]
        public void Decrement_FromZero_WrapsAndSetsBorrowFlag()
        {
            var result = this.gameboy.Cpu.Operations.Decrement(0x00);

            Assert.AreEqual(0xFF, result);
            AssertFlags(0x60);
        }

        [TestMethod]
        public void Decrement_ToZero_SetsZeroFlag()
        {
            var result = this.gameboy.Cpu.Operations.Decrement(0x01);

            Assert.AreEqual(0x00, result);
            AssertFlags(0xC0);
        }

        [TestMethod]
        public void Increment_SetsHalfCarryWhenLowNibbleOverflows()
        {
            var result = this.gameboy.Cpu.Operations.Increment(0x0F);

            Assert.AreEqual(0x10, result);
            AssertFlags(0x20);
        }

        [TestMethod]
        public void Increment_FromMax_WrapsAndSetsZero()
        {
            var result = this.gameboy.Cpu.Operations.Increment(0xFF);

            Assert.AreEqual(0x00, result);
            AssertFlags(0xA0);
        }

        [TestMethod]
        public void Add_SetsHalfCarryWithoutFullCarry()
        {
            var result = this.gameboy.Cpu.Operations.Add(0x0F, 0x01);

            Assert.AreEqual(0x10, result);
            AssertFlags(0x20);
        }

        [TestMethod]
        public void Add_WithOverflow_SetsCarryAndZero()
        {
            var result = this.gameboy.Cpu.Operations.Add(0xFF, 0x01);

            Assert.AreEqual(0x00, result);
            AssertFlags(0xB0);
        }
    }
}
