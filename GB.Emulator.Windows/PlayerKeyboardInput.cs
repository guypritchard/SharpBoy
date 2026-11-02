using GB.Emulator.Core.InputOutput;

namespace GB.Emulator.Windows;

/// <summary>Consumes game keys before menus/buttons can treat them as navigation.</summary>
internal sealed class PlayerKeyboardInput(Form form, IButtonInput buttons, Func<bool> enabled,
    Func<Keys>? modifiers = null) : IMessageFilter
{
    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg is not (0x100 or 0x101) || !enabled() ||
            Control.FromChildHandle(message.HWnd)?.FindForm() != form) return false;
        if (message.Msg == 0x100 && ((modifiers?.Invoke() ?? Control.ModifierKeys) & (Keys.Control | Keys.Alt)) != Keys.None)
            return false;
        if (!TryMap((Keys)(int)message.WParam, out GameBoyButton button)) return false;
        buttons.SetButtonState(button, message.Msg == 0x100);
        return true;
    }

    internal static bool TryMap(Keys key, out GameBoyButton button)
    {
        button = (key & Keys.KeyCode) switch
        {
            Keys.Right => GameBoyButton.Right, Keys.Left => GameBoyButton.Left,
            Keys.Up => GameBoyButton.Up, Keys.Down => GameBoyButton.Down,
            Keys.Z => GameBoyButton.A, Keys.X => GameBoyButton.B,
            Keys.Enter => GameBoyButton.Start, Keys.Space => GameBoyButton.Select,
            _ => 0
        };
        return button != 0;
    }
}
