using System;
using System.Runtime.InteropServices;

namespace GB.Emulator;

internal interface IConsoleKeyReader
{
    bool TryRead(out ConsoleKey key);
}

internal interface IPhysicalKeyboardState
{
    bool IsDown(ConsoleKey key);
}

internal sealed class ConsoleKeyReader : IConsoleKeyReader
{
    public bool TryRead(out ConsoleKey key)
    {
        if (Console.IsInputRedirected || !Console.KeyAvailable)
        {
            key = default;
            return false;
        }
        key = Console.ReadKey(intercept: true).Key;
        return true;
    }
}

internal sealed class WindowsPhysicalKeyboardState : IPhysicalKeyboardState
{
    public bool IsDown(ConsoleKey key) => GetAsyncKeyState(key switch
    {
        ConsoleKey.LeftArrow => 0x25,
        ConsoleKey.UpArrow => 0x26,
        ConsoleKey.RightArrow => 0x27,
        ConsoleKey.DownArrow => 0x28,
        ConsoleKey.Spacebar => 0x20,
        ConsoleKey.Enter => 0x0D,
        ConsoleKey.Z => 0x5A,
        ConsoleKey.X => 0x58,
        ConsoleKey.R => 0x52,
        _ => 0
    }) < 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
