using GB.Emulator.Core;
using GB.Emulator.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace GB.Emulator.Tests
{
    [TestClass]
    public class DmgScreenRendererTests
    {
        private static byte[] Memory(byte lcdc = 0x91)
        {
            var memory = new byte[0x10000];
            memory[0xFF40] = lcdc;
            memory[0xFF47] = 0xE4;
            memory[0xFF48] = 0xE4;
            memory[0xFF49] = 0xE4;
            return memory;
        }

        private static byte[] Screen(byte[] memory)
        {
            var pixels = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
            DmgScreenRenderer.RenderScreen(new VideoState(memory), pixels);
            return pixels;
        }

        [TestMethod]
        public void BundledSpriteRomReachesAVisibleScreenAfterHighRamDma()
        {
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = File.ReadAllBytes(Roms.TestRom) });
            int completedFrames = 0;
            gameboy.Video.FrameReady += (_, _) => completedFrames++;

            for (int i = 0; i < 100000; i++) gameboy.Step();

            byte[] memory = gameboy.Memory.Snapshot();
            byte[] screen = Screen(memory);
            Assert.AreNotEqual(0, memory[0xFF40] & 0x80, "ROM should enable the LCD.");
            Assert.AreNotEqual(0, completedFrames, "The video hardware should request redraws.");
            Assert.IsTrue(Array.Exists(screen, shade => shade != screen[0]),
                "The rendered screen should contain more than one shade.");
        }

        [TestMethod]
        public void BackgroundUsesTileMapAndTwoBitTileData()
        {
            byte[] memory = Memory();
            memory[0x8000] = 0x80; // Color 1 at x=0.
            memory[0x8001] = 0x40; // Color 2 at x=1.

            byte[] screen = Screen(memory);
            Assert.AreEqual(1, screen[0]);
            Assert.AreEqual(2, screen[1]);
            Assert.AreEqual(0, screen[2]);
        }

        [TestMethod]
        public void SignedTileModeUsesTilesAt9000()
        {
            byte[] memory = Memory(0x81);
            memory[0x9000] = 0x80;

            Assert.AreEqual(1, Screen(memory)[0]);
        }

        [TestMethod]
        public void BackgroundScrollAndWindowSelectDifferentMapTiles()
        {
            byte[] memory = Memory(0x91);
            memory[0xFF43] = 8;
            memory[0x9801] = 1;
            memory[0x8011] = 0x80; // Tile 1, color 2 at x=0.
            Assert.AreEqual(2, Screen(memory)[0]);

            memory[0xFF40] = 0xF1; // Window enabled, map at 0x9C00.
            memory[0xFF4A] = 0;
            memory[0xFF4B] = 7;
            memory[0x9C00] = 2;
            memory[0x8020] = 0x80;
            memory[0x8021] = 0x80;
            Assert.AreEqual(3, Screen(memory)[0]);
        }

        [TestMethod]
        public void SpritePaletteAndBackgroundPriorityAreApplied()
        {
            byte[] memory = Memory(0x93);
            memory[0x8000] = 0x80; // Background color 1.
            memory[0x8011] = 0x80; // Sprite tile 1, color 2.
            memory[0xFE00] = 16;
            memory[0xFE01] = 8;
            memory[0xFE02] = 1;
            memory[0xFF48] = 0xD4; // Sprite color 2 maps to shade 1.

            Assert.AreEqual(1, Screen(memory)[0]);
            memory[0xFF48] = 0xE4;
            Assert.AreEqual(2, Screen(memory)[0]);
            memory[0xFE03] = 0x80; // Behind nonzero background color.
            Assert.AreEqual(1, Screen(memory)[0]);
        }

        [TestMethod]
        public void HigherPrioritySpriteCanBeHiddenBehindBackground()
        {
            byte[] memory = Memory(0x93);
            memory[0x8000] = 0x80; // Background color 1.
            memory[0x8011] = 0x80; // First sprite color 2.
            memory[0x8020] = 0x80; // Second sprite color 1.
            memory[0x8021] = 0x80; // Now color 3.
            memory[0xFE00] = 16;
            memory[0xFE01] = 8;
            memory[0xFE02] = 1;
            memory[0xFE03] = 0x80; // The first sprite is behind the background.
            memory[0xFE04] = 16;
            memory[0xFE05] = 8;
            memory[0xFE06] = 2;

            // OAM order selects the first sprite before BG priority is applied.
            Assert.AreEqual(1, Screen(memory)[0]);
        }

        [TestMethod]
        public void TileAtlasIncludesFinalTileAt97F0()
        {
            byte[] memory = Memory();
            memory[0x97F0] = 0x80;
            var atlas = new byte[DmgScreenRenderer.TileAtlasWidth * DmgScreenRenderer.TileAtlasHeight];

            DmgScreenRenderer.RenderTileAtlas(new VideoState(memory), atlas);

            int lastTileX = 15 * 8;
            int lastTileY = 23 * 8;
            Assert.AreEqual(1, atlas[lastTileY * DmgScreenRenderer.TileAtlasWidth + lastTileX]);
        }

        [TestMethod]
        public void LcdOffProducesBlankFrameAndInvalidBuffersAreRejected()
        {
            byte[] memory = Memory(0x11);
            var screen = new byte[DmgScreenRenderer.Width * DmgScreenRenderer.Height];
            Array.Fill(screen, (byte)3);
            DmgScreenRenderer.RenderScreen(new VideoState(memory), screen);
            Assert.AreEqual(0, screen[0]);
            Assert.AreEqual(0, screen[^1]);
            Assert.ThrowsExactly<ArgumentException>(() => new VideoState(new byte[1]));
        }

        [TestMethod]
        public void LoadingAnotherRomClearsVideoMemoryAndLcdState()
        {
            var gameboy = new Gameboy();
            var cartridge = new Cartridge { Data = new byte[0x200] };
            gameboy.Load(cartridge);
            gameboy.Memory.Write8(0x80, 0xFF40);
            gameboy.Memory.Write8(0xFF, 0x8000);
            gameboy.Memory.Write8(0x12, 0x9800);

            gameboy.Load(cartridge);

            Assert.AreEqual(0, gameboy.Memory.Read8(0xFF40));
            Assert.AreEqual(0, gameboy.Memory.Read8(0x8000));
            Assert.AreEqual(0, gameboy.Memory.Read8(0x9800));
        }

        [TestMethod]
        public void OamDmaCopiesSpriteEntriesIntoScreenMemory()
        {
            var gameboy = new Gameboy();
            gameboy.Load(new Cartridge { Data = new byte[0x200] });
            gameboy.Memory.Write8(16, 0xC000);
            gameboy.Memory.Write8(8, 0xC001);
            gameboy.Memory.Write8(1, 0xC002);

            gameboy.Memory.Write8(0xC0, 0xFF46);

            Assert.AreEqual(16, gameboy.Memory.Peek(0xFE00));
            Assert.AreEqual(8, gameboy.Memory.Peek(0xFE01));
            Assert.AreEqual(1, gameboy.Memory.Peek(0xFE02));
        }
    }
}
