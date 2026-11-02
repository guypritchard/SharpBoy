using GB.Sdk;

static class ScrollingBackground
{
    [GameEntryPoint]
    public static void Start()
    {
        GameDisplay.InitializeBackground();
        byte scroll = 0;
        while (true)
        {
            GameDisplay.WaitForNextVBlank();
            scroll++;
            GameDisplay.SetScroll(scroll, scroll);
        }
    }
}
