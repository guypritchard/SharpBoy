using GB.Emulator.Core;
using GB.Emulator.Core.InputOutput;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class HardwareBoundaryTests
    {
        [TestMethod]
        public void VideoStateIsASnapshotOfVideoHardware()
        {
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = new byte[0x200] });
            gameboy.Memory.Write8(0x80, 0x8000);
            gameboy.Memory.Write8(0x91, 0xFF40);

            VideoState captured = gameboy.CaptureVideoState();
            gameboy.Memory.Write8(0x00, 0x8000);
            gameboy.Memory.Write8(0x00, 0xFF40);

            Assert.AreEqual(0x80, captured[0x8000]);
            Assert.AreEqual(0x91, captured.LcdControl);
            Assert.AreEqual(0x00, gameboy.CaptureVideoState()[0x8000]);
        }

        [TestMethod]
        public void VideoSignalsEachCompletedFrame()
        {
            var lcd = new Lcd();
            var video = new Video(lcd);
            int frames = 0;
            video.FrameReady += (_, _) => frames++;
            lcd.Write8(0xFF40, 0x80);

            video.Step(Lcd.CyclesPerScanline * 143);
            Assert.AreEqual(0, frames);
            video.Step(Lcd.CyclesPerScanline);
            Assert.AreEqual(1, frames);
            video.Step(Lcd.CyclesPerScanline * Lcd.ScanlinesPerFrame);
            Assert.AreEqual(2, frames);
        }

        [TestMethod]
        public void SoundRegistersAreMappedAndRestoredWithHardwareState()
        {
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = new byte[0x200] });
            gameboy.Memory.Write8(0x7A, 0xFF12);
            SoundState sound = gameboy.CaptureSoundState();
            GameboyState saved = gameboy.CaptureState();

            gameboy.Memory.Write8(0x31, 0xFF12);
            Assert.AreEqual(0x7A, sound[0xFF12]);
            Assert.AreEqual(0x31, gameboy.Sound.Read8(0xFF12));

            gameboy.RestoreState(saved);
            Assert.AreEqual(0x7A, gameboy.Memory.Read8(0xFF12));
        }

        [TestMethod]
        public void LoadingAnotherCartridgeResetsMappedHardware()
        {
            var gameboy = new Gameboy();
            var cartridge = new Cartridge { Data = new byte[0x200] };
            gameboy.Load(cartridge);
            gameboy.Memory.Write8(0x7A, 0xFF12);
            gameboy.Memory.Write8(0x55, 0xC000);
            gameboy.Memory.Write8(0x1F, 0xFFFF);

            gameboy.Load(cartridge);

            Assert.AreEqual(0, gameboy.Memory.Read8(0xFF12));
            Assert.AreEqual(0, gameboy.Memory.Read8(0xC000));
            Assert.AreEqual(0, gameboy.Memory.Read8(0xFFFF));
        }
    }
}
