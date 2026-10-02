#nullable enable
using System;
using System.Collections.Generic;
using GB.Emulator.Core.InputOutput;

namespace GB.Emulator;

/// <summary>Maps console presses to Game Boy buttons and releases them with the physical key.</summary>
internal sealed class ConsoleKeyboardInput : IDisposable
{
    private const int MinimumTapMilliseconds = 30;
    private const int FallbackHoldMilliseconds = 150;
    private static readonly GameBoyButton[] AllButtons =
    {
        GameBoyButton.Right, GameBoyButton.Left, GameBoyButton.Up, GameBoyButton.Down,
        GameBoyButton.A, GameBoyButton.B, GameBoyButton.Select, GameBoyButton.Start
    };

    private readonly IButtonInput buttons;
    private readonly IConsoleKeyReader keys;
    private readonly IPhysicalKeyboardState? physicalKeys;
    private readonly TimeProvider time;
    private readonly Dictionary<GameBoyButton, HeldKey> held = new();
    private bool redrawHeld;
    private long redrawExpiresAt;

    public ConsoleKeyboardInput(IButtonInput buttons)
        : this(buttons, new ConsoleKeyReader(),
            OperatingSystem.IsWindows() ? new WindowsPhysicalKeyboardState() : null,
            TimeProvider.System)
    {
    }

    internal ConsoleKeyboardInput(IButtonInput buttons, IConsoleKeyReader keys,
        IPhysicalKeyboardState? physicalKeys, TimeProvider time)
    {
        this.buttons = buttons;
        this.keys = keys;
        this.physicalKeys = physicalKeys;
        this.time = time;
    }

    public bool Poll()
    {
        bool redraw = false;
        long now = this.time.GetTimestamp();
        while (this.keys.TryRead(out ConsoleKey key))
        {
            if (key == ConsoleKey.R)
            {
                if (!this.redrawHeld) redraw = true;
                this.redrawHeld = true;
                this.redrawExpiresAt = now + this.Milliseconds(FallbackHoldMilliseconds);
            }
            else if (TryMap(key, out GameBoyButton button))
            {
                if (this.held.TryGetValue(button, out HeldKey previous))
                {
                    this.held[button] = previous with
                    {
                        FallbackReleaseAt = now + this.Milliseconds(FallbackHoldMilliseconds)
                    };
                }
                else
                {
                    this.buttons.SetButtonState(button, true);
                    this.held.Add(button, new HeldKey(key, now + this.Milliseconds(MinimumTapMilliseconds),
                        now + this.Milliseconds(FallbackHoldMilliseconds)));
                }
            }
        }

        foreach (GameBoyButton button in AllButtons)
        {
            if (!this.held.TryGetValue(button, out HeldKey press) || now < press.EarliestReleaseAt)
                continue;
            bool released = this.physicalKeys is null
                ? now >= press.FallbackReleaseAt
                : !this.physicalKeys.IsDown(press.Key);
            if (!released) continue;
            this.buttons.SetButtonState(button, false);
            this.held.Remove(button);
        }

        if (this.redrawHeld &&
            (this.physicalKeys is null ? now >= this.redrawExpiresAt : !this.physicalKeys.IsDown(ConsoleKey.R)))
            this.redrawHeld = false;
        return redraw;
    }

    public void Dispose()
    {
        this.buttons.ReleaseAll();
        this.held.Clear();
    }

    private long Milliseconds(int milliseconds) => this.time.TimestampFrequency * milliseconds / 1000;

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

    private readonly record struct HeldKey(ConsoleKey Key, long EarliestReleaseAt, long FallbackReleaseAt);
}
