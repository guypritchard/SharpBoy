using System;
using System.Collections.Generic;
using System.Diagnostics;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator
{
    /// <summary>Turns terminal key presses into short, repeatable button holds.</summary>
    internal sealed class ConsoleKeyboardInput : IDisposable
    {
        private const int HoldMilliseconds = 150;
        private static readonly GameBoyButton[] AllButtons =
        {
            GameBoyButton.Right, GameBoyButton.Left, GameBoyButton.Up, GameBoyButton.Down,
            GameBoyButton.A, GameBoyButton.B, GameBoyButton.Select, GameBoyButton.Start
        };
        private readonly IButtonInput buttons;
        private readonly Dictionary<GameBoyButton, long> releaseAt = new();

        public ConsoleKeyboardInput(IButtonInput buttons) => this.buttons = buttons;

        public bool Poll()
        {
            bool redraw = false;
            if (!Console.IsInputRedirected)
            {
                while (Console.KeyAvailable)
                {
                    ConsoleKey key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.R)
                    {
                        redraw = true;
                    }
                    else if (TryMap(key, out GameBoyButton button))
                    {
                        this.buttons.SetButtonState(button, true);
                        this.releaseAt[button] = Stopwatch.GetTimestamp() +
                            Stopwatch.Frequency * HoldMilliseconds / 1000;
                    }
                }
            }

            long now = Stopwatch.GetTimestamp();
            foreach (GameBoyButton button in AllButtons)
            {
                if (!this.releaseAt.TryGetValue(button, out long deadline) || now < deadline) continue;
                this.buttons.SetButtonState(button, false);
                this.releaseAt.Remove(button);
            }

            return redraw;
        }

        public void Dispose()
        {
            this.buttons.ReleaseAll();
            this.releaseAt.Clear();
        }

        private static bool TryMap(ConsoleKey key, out GameBoyButton button)
        {
            button = key switch
            {
                ConsoleKey.RightArrow => GameBoyButton.Right,
                ConsoleKey.LeftArrow => GameBoyButton.Left,
                ConsoleKey.UpArrow => GameBoyButton.Up,
                ConsoleKey.DownArrow => GameBoyButton.Down,
                ConsoleKey.Z => GameBoyButton.A,
                ConsoleKey.X => GameBoyButton.B,
                ConsoleKey.Spacebar => GameBoyButton.Select,
                ConsoleKey.Enter => GameBoyButton.Start,
                _ => 0
            };
            return button != 0;
        }
    }
}
