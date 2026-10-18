using GB.Sdk;

static class Sprites
{
    [GameEntryPoint]
    public static void Start()
    {
        GameDisplay.InitializeBackground();
        GameSprites.Initialize();
        byte x = 32;
        GameSprites.Set(0, x, 56, 0);
        byte frames = 0;
        while (true)
        {
            GameDisplay.WaitForNextVBlank();
            x++;
            GameSprites.Set(0, x, 56, 0);
            frames++;
            if (frames == 8) GameMemory.WriteByte(0xC200, 0xA5);
        }
    }
}
