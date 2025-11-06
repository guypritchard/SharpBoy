using GB.Emulator.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class CpuOperationsTests
    {
        [TestInitialize]
        public void ResetCpuState()
        {
            Cpu.Registers.Reset();
        }

        private static void AssertFlags(byte expected)
        {
            Assert.AreEqual(expected, Cpu.Registers.Flags, $"Flags: 0x{Cpu.Registers.Flags:X2}");
        }

        [TestMethod]
        public void Decrement_FromZero_WrapsAndSetsBorrowFlag()
        {
            var result = Cpu.Operations.Decrement(0x00);

            Assert.AreEqual(0xFF, result);
            AssertFlags(0x60);
        }

        [TestMethod]
        public void Decrement_ToZero_SetsZeroFlag()
        {
            var result = Cpu.Operations.Decrement(0x01);

            Assert.AreEqual(0x00, result);
            AssertFlags(0xC0);
        }

        [TestMethod]
        public void Increment_SetsHalfCarryWhenLowNibbleOverflows()
        {
            var result = Cpu.Operations.Increment(0x0F);

            Assert.AreEqual(0x10, result);
            AssertFlags(0x20);
        }

        [TestMethod]
        public void Increment_FromMax_WrapsAndSetsZero()
        {
            var result = Cpu.Operations.Increment(0xFF);

            Assert.AreEqual(0x00, result);
            AssertFlags(0xA0);
        }

        [TestMethod]
        public void Add_SetsHalfCarryWithoutFullCarry()
        {
            var result = Cpu.Operations.Add(0x0F, 0x01);

            Assert.AreEqual(0x10, result);
            AssertFlags(0x20);
        }

        [TestMethod]
        public void Add_WithOverflow_SetsCarryAndZero()
        {
            var result = Cpu.Operations.Add(0xFF, 0x01);

            Assert.AreEqual(0x00, result);
            AssertFlags(0xB0);
        }
    }
}
