#nullable enable
using System;

namespace GB.Emulator.Core.InputOutput
{
    /// <summary>The eight buttons and the active-low P1 matrix at FF00.</summary>
    public sealed class Joypad : IMemoryRange, IButtonInput
    {
        private byte selection = 0x30;
        private byte pressedButtons;

        public ushort Start => 0xFF00;
        public ushort End => 0xFF00;

        public event EventHandler? InterruptRequested;

        public byte Read8(ushort location)
        {
            if (location != Start) throw new ArgumentOutOfRangeException(nameof(location));
            int lines = 0x0F;
            if ((this.selection & 0x10) == 0)
                lines &= ~(this.pressedButtons & 0x0F);
            if ((this.selection & 0x20) == 0)
                lines &= ~((this.pressedButtons >> 4) & 0x0F);
            return (byte)(0xC0 | this.selection | lines);
        }

        public void Write8(ushort location, byte value)
        {
            if (location != Start) throw new ArgumentOutOfRangeException(nameof(location));
            byte before = this.Read8(Start);
            this.selection = (byte)(value & 0x30);
            this.RequestInterruptForNewLowLines(before);
        }

        public void SetButtonState(GameBoyButton button, bool pressed)
        {
            ValidateButton(button);
            byte before = this.Read8(Start);
            if (pressed)
                this.pressedButtons |= (byte)button;
            else
                this.pressedButtons &= (byte)~(byte)button;
            this.RequestInterruptForNewLowLines(before);
        }

        public bool IsPressed(GameBoyButton button)
        {
            ValidateButton(button);
            return (this.pressedButtons & (byte)button) != 0;
        }

        public void ReleaseAll() => this.pressedButtons = 0;

        internal void Reset()
        {
            this.selection = 0x30;
            this.pressedButtons = 0;
        }

        internal JoypadState Snapshot() => new(this.selection, this.pressedButtons);

        internal void Restore(JoypadState state)
        {
            this.selection = state.Selection;
            this.pressedButtons = state.PressedButtons;
        }

        private void RequestInterruptForNewLowLines(byte before)
        {
            if ((before & ~this.Read8(Start) & 0x0F) != 0)
                this.InterruptRequested?.Invoke(this, EventArgs.Empty);
        }

        private static void ValidateButton(GameBoyButton button)
        {
            byte value = (byte)button;
            if (value == 0 || (value & (value - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(button), "Choose one Game Boy button.");
        }

        internal readonly record struct JoypadState(byte Selection, byte PressedButtons);
    }
}
