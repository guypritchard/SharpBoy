using System;
using GB.Emulator.Core;

namespace GB.Emulator.Display
{
    /// <summary>
    /// Builds a DMG screen preview from current memory contents. This is a
    /// snapshot of VRAM, OAM, and LCD registers, not a scanline-accurate PPU.
    /// </summary>
    public static class DmgScreenRenderer
    {
        public const int Width = 160;
        public const int Height = 144;
        public const int TileColumns = 16;
        public const int TileRows = 24;
        public const int TileAtlasWidth = TileColumns * 8;
        public const int TileAtlasHeight = TileRows * 8;

        public static void RenderScreen(VideoState memory, Span<byte> shades)
        {
            ArgumentNullException.ThrowIfNull(memory);
            Validate(shades, Width * Height);
            byte lcdc = memory[0xFF40];
            if ((lcdc & 0x80) == 0)
            {
                shades.Slice(0, Width * Height).Clear();
                return;
            }

            byte bgPalette = memory[0xFF47];
            byte objPalette0 = memory[0xFF48];
            byte objPalette1 = memory[0xFF49];
            int scrollX = memory[0xFF43];
            int scrollY = memory[0xFF42];
            int windowX = memory[0xFF4B] - 7;
            int windowY = memory[0xFF4A];
            bool bgEnabled = (lcdc & 0x01) != 0;
            bool objectsEnabled = (lcdc & 0x02) != 0;
            bool windowEnabled = bgEnabled && (lcdc & 0x20) != 0;
            int objectHeight = (lcdc & 0x04) != 0 ? 16 : 8;

            Span<int> visibleObjects = stackalloc int[10];
            for (int y = 0; y < Height; y++)
            {
                int objectCount = 0;
                if (objectsEnabled)
                {
                    for (int objectIndex = 0; objectIndex < 40 && objectCount < 10; objectIndex++)
                    {
                        int oam = 0xFE00 + objectIndex * 4;
                        int objectY = memory[oam] - 16;
                        if (y >= objectY && y < objectY + objectHeight)
                            visibleObjects[objectCount++] = oam;
                    }
                }

                for (int x = 0; x < Width; x++)
                {
                    int backgroundColor = 0;
                    if (bgEnabled)
                    {
                        bool useWindow = windowEnabled && y >= windowY && x >= windowX;
                        int mapX = useWindow ? x - windowX : (x + scrollX) & 0xFF;
                        int mapY = useWindow ? y - windowY : (y + scrollY) & 0xFF;
                        int mapBase = useWindow
                            ? ((lcdc & 0x40) != 0 ? 0x9C00 : 0x9800)
                            : ((lcdc & 0x08) != 0 ? 0x9C00 : 0x9800);
                        int tileId = memory[mapBase + ((mapY >> 3) * 32) + (mapX >> 3)];
                        int tileAddress = (lcdc & 0x10) != 0
                            ? 0x8000 + tileId * 16
                            : 0x9000 + (sbyte)tileId * 16;
                        backgroundColor = ReadTileColor(memory, tileAddress, mapX & 7, mapY & 7);
                    }

                    int shade = PaletteShade(bgPalette, backgroundColor);
                    int chosenX = int.MaxValue;
                    int chosenIndex = int.MaxValue;
                    int chosenColor = 0;
                    byte chosenAttributes = 0;
                    for (int i = 0; i < objectCount; i++)
                    {
                        int oam = visibleObjects[i];
                        int objectX = memory[oam + 1] - 8;
                        if (x < objectX || x >= objectX + 8) continue;
                        int pixelX = x - objectX;
                        int pixelY = y - (memory[oam] - 16);
                        byte attributes = memory[oam + 3];
                        if ((attributes & 0x20) != 0) pixelX = 7 - pixelX;
                        if ((attributes & 0x40) != 0) pixelY = objectHeight - 1 - pixelY;
                        int tileId = memory[oam + 2];
                        if (objectHeight == 16) tileId &= 0xFE;
                        int color = ReadTileColor(memory, 0x8000 + tileId * 16, pixelX, pixelY);
                        if (color == 0) continue;

                        // On DMG, lower X wins; OAM order breaks ties.
                        int index = (oam - 0xFE00) / 4;
                        if (objectX < chosenX || (objectX == chosenX && index < chosenIndex))
                        {
                            chosenX = objectX;
                            chosenIndex = index;
                            chosenColor = color;
                            chosenAttributes = attributes;
                        }
                    }

                    if (chosenColor != 0 && ((chosenAttributes & 0x80) == 0 || backgroundColor == 0))
                        shade = PaletteShade((chosenAttributes & 0x10) != 0 ? objPalette1 : objPalette0,
                            chosenColor);

                    shades[y * Width + x] = (byte)shade;
                }
            }
        }

        public static void RenderTileAtlas(VideoState memory, Span<byte> shades)
        {
            ArgumentNullException.ThrowIfNull(memory);
            Validate(shades, TileAtlasWidth * TileAtlasHeight);
            for (int tile = 0; tile < TileColumns * TileRows; tile++)
            {
                int tileX = (tile % TileColumns) * 8;
                int tileY = (tile / TileColumns) * 8;
                int tileAddress = 0x8000 + tile * 16;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        shades[(tileY + y) * TileAtlasWidth + tileX + x] =
                            (byte)ReadTileColor(memory, tileAddress, x, y);
                    }
                }
            }
        }

        private static int ReadTileColor(VideoState memory, int tileAddress, int x, int y)
        {
            int rowAddress = tileAddress + y * 2;
            int bit = 7 - x;
            return ((memory[rowAddress] >> bit) & 1) | (((memory[rowAddress + 1] >> bit) & 1) << 1);
        }

        private static int PaletteShade(byte palette, int color) => (palette >> (color * 2)) & 3;

        private static void Validate(Span<byte> shades, int pixelCount)
        {
            if (shades.Length < pixelCount)
                throw new ArgumentException($"At least {pixelCount} output pixels are required.", nameof(shades));
        }
    }
}
