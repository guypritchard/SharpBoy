using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class JoypadTests
    {
        private static Gameboy CreateGameboy()
        {
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = new byte[0x200] });
            return gameboy;
        }

        [TestMethod]
        public void SelectedRowsReadPressedButtonsAsLowBits()
        {
            var gameboy = CreateGameboy();
            gameboy.Input.SetButtonState(GameBoyButton.Right, true);
            gameboy.Input.SetButtonState(GameBoyButton.B, true);
            Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xFF00));

            gameboy.Memory.Write8(0x20, 0xFF00); // Direction row selected.
            Assert.AreEqual(0xEE, gameboy.Memory.Read8(0xFF00));

            gameboy.Memory.Write8(0x10, 0xFF00); // Action row selected.
            Assert.AreEqual(0xDD, gameboy.Memory.Read8(0xFF00));

            gameboy.Memory.Write8(0x00, 0xFF00); // Both rows selected.
            Assert.AreEqual(0xCC, gameboy.Memory.Read8(0xFF00));
            Assert.AreEqual(0xCC, gameboy.Memory.Peek(0xFF00));
            Assert.AreEqual(0xCC, gameboy.Memory.Snapshot()[0xFF00]);
        }

        [TestMethod]
        public void NewLowLineRequestsJoypadInterruptOnce()
        {
            var gameboy = CreateGameboy();
            gameboy.Memory.Write8(0x20, 0xFF00);

            gameboy.Input.SetButtonState(GameBoyButton.Right, true);
            Assert.AreEqual(0x10, gameboy.Memory.Peek(0xFF0F) & 0x10);

            gameboy.Memory.Write8(0, 0xFF0F);
            gameboy.Input.SetButtonState(GameBoyButton.Right, true);
            gameboy.Input.SetButtonState(GameBoyButton.Right, false);
            Assert.AreEqual(0, gameboy.Memory.Peek(0xFF0F) & 0x10);

            gameboy.Input.SetButtonState(GameBoyButton.Right, true);
            Assert.AreEqual(0x10, gameboy.Memory.Peek(0xFF0F) & 0x10);
        }

        [TestMethod]
        public void SelectingAHeldButtonRequestsInterruptAndStateCanBeRestored()
        {
            var gameboy = CreateGameboy();
            gameboy.Input.SetButtonState(GameBoyButton.A, true);
            Assert.AreEqual(0, gameboy.Memory.Peek(0xFF0F) & 0x10);

            gameboy.Memory.Write8(0x10, 0xFF00);
            Assert.AreEqual(0x10, gameboy.Memory.Peek(0xFF0F) & 0x10);
            GameboyState saved = gameboy.CaptureState();

            gameboy.Input.ReleaseAll();
            Assert.AreEqual(0xDF, gameboy.Memory.Read8(0xFF00));
            gameboy.RestoreState(saved);
            Assert.IsTrue(gameboy.Input.IsPressed(GameBoyButton.A));
            Assert.AreEqual(0xDE, gameboy.Memory.Read8(0xFF00));

            gameboy.Load(new Cartridge { Data = new byte[0x200] });
            Assert.AreEqual(0xFF, gameboy.Memory.Read8(0xFF00));
            Assert.IsFalse(gameboy.Input.IsPressed(GameBoyButton.A));
        }

        [TestMethod]
        public void CpuCanReadKeyboardButtonThroughJoypadRegister()
        {
            var rom = new byte[0x200];
            rom[0x100] = 0x3E; // LD A,20
            rom[0x101] = 0x20;
            rom[0x102] = 0xE0; // LDH (FF00),A: select directions
            rom[0x103] = 0x00;
            rom[0x104] = 0xF0; // LDH A,(FF00)
            rom[0x105] = 0x00;
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = rom });
            gameboy.Input.SetButtonState(GameBoyButton.Right, true);

            gameboy.Step();
            gameboy.Step();
            gameboy.Step();

            Assert.AreEqual(0xEE, Cpu.Registers.A);
        }

        [TestMethod]
        public void SelectedButtonPressWakesCpuFromStop()
        {
            var rom = new byte[0x200];
            rom[0x100] = 0x10; // STOP
            rom[0x101] = 0x00;
            rom[0x102] = 0x00; // NOP after wake
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = rom });
            gameboy.Memory.Write8(0x20, 0xFF00);

            gameboy.Step();
            Assert.AreEqual("STOP idle", gameboy.Step().Instruction.Name);
            gameboy.Input.SetButtonState(GameBoyButton.Right, true);

            Assert.AreEqual("NOP", gameboy.Step().Instruction.Name);
        }
    }
}
