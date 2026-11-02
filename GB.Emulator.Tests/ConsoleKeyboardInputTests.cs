using System;
using System.Collections.Generic;
using GB.Emulator;
using GB.Emulator.Core.InputOutput;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GB.Emulator.Tests;

[TestClass]
public class ConsoleKeyboardInputTests
{
    [TestMethod]
    public void HeldKeyStaysPressedUntilPhysicalRelease()
    {
        var buttons = new Joypad();
        var keys = new FakeKeyReader();
        var physical = new FakePhysicalKeyboard();
        var time = new ManualTime();
        using var input = new ConsoleKeyboardInput(buttons, keys, physical, time);

        physical.Down.Add(ConsoleKey.RightArrow);
        keys.Enqueue(ConsoleKey.RightArrow);
        input.Poll();
        time.Advance(1000);
        input.Poll();
        Assert.IsTrue(buttons.IsPressed(GameBoyButton.Right));

        physical.Down.Clear();
        keys.Enqueue(ConsoleKey.RightArrow); // A queued repeat must not extend a released key.
        input.Poll();
        Assert.IsFalse(buttons.IsPressed(GameBoyButton.Right));
    }

    [TestMethod]
    public void QuickTapRemainsVisibleForTwoFrames()
    {
        var buttons = new Joypad();
        var keys = new FakeKeyReader();
        var time = new ManualTime();
        using var input = new ConsoleKeyboardInput(buttons, keys, new FakePhysicalKeyboard(), time);

        keys.Enqueue(ConsoleKey.Z);
        input.Poll();
        time.Advance(29);
        input.Poll();
        Assert.IsTrue(buttons.IsPressed(GameBoyButton.A));

        time.Advance(1);
        input.Poll();
        Assert.IsFalse(buttons.IsPressed(GameBoyButton.A));
    }

    [TestMethod]
    public void FallbackExtendsHoldOnKeyRepeatAndRedrawDoesNotRepeat()
    {
        var buttons = new Joypad();
        var keys = new FakeKeyReader();
        var time = new ManualTime();
        using var input = new ConsoleKeyboardInput(buttons, keys, null, time);

        keys.Enqueue(ConsoleKey.LeftArrow, ConsoleKey.R);
        Assert.IsTrue(input.Poll());
        time.Advance(100);
        keys.Enqueue(ConsoleKey.LeftArrow, ConsoleKey.R);
        Assert.IsFalse(input.Poll());
        time.Advance(100);
        input.Poll();
        Assert.IsTrue(buttons.IsPressed(GameBoyButton.Left));

        time.Advance(50);
        input.Poll();
        Assert.IsFalse(buttons.IsPressed(GameBoyButton.Left));
        keys.Enqueue(ConsoleKey.R);
        Assert.IsTrue(input.Poll());
    }

    private sealed class FakeKeyReader : IConsoleKeyReader
    {
        private readonly Queue<ConsoleKey> pending = new();
        public void Enqueue(params ConsoleKey[] keys)
        {
            foreach (ConsoleKey key in keys) this.pending.Enqueue(key);
        }
        public bool TryRead(out ConsoleKey key)
        {
            if (this.pending.Count == 0) { key = default; return false; }
            key = this.pending.Dequeue();
            return true;
        }
    }

    private sealed class FakePhysicalKeyboard : IPhysicalKeyboardState
    {
        public readonly HashSet<ConsoleKey> Down = new();
        public bool IsDown(ConsoleKey key) => this.Down.Contains(key);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => this.now;
        public void Advance(int milliseconds) => this.now += milliseconds;
    }
}
