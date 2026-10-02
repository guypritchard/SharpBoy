using GB.Emulator.Core.InputOutput;

namespace GB.Debugger;

/// <summary>Maps WinForms key down/up events to the Game Boy buttons.</summary>
internal sealed class DebuggerKeyboardInput : IMessageFilter
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private readonly Form form;
    private readonly IButtonInput buttons;
    private readonly Func<bool> enabled;
    private readonly Action changed;

    public DebuggerKeyboardInput(Form form, IButtonInput buttons, Func<bool> enabled, Action changed)
    {
        this.form = form;
        this.buttons = buttons;
        this.enabled = enabled;
        this.changed = changed;
    }

    public bool PreFilterMessage(ref Message message)
    {
        if ((message.Msg != WmKeyDown && message.Msg != WmKeyUp) || !this.enabled())
            return false;
        if (Control.FromChildHandle(message.HWnd)?.FindForm() != this.form)
            return false;
        if (message.Msg == WmKeyDown &&
            (Control.ModifierKeys & (Keys.Control | Keys.Alt)) != Keys.None)
            return false;

        Keys key = (Keys)(int)message.WParam;
        bool handled = message.Msg == WmKeyDown ? this.KeyDown(key) : this.KeyUp(key);
        if (handled) this.changed();
        return handled;
    }

    public bool KeyDown(Keys key)
    {
        if (!TryMap(key, out GameBoyButton button)) return false;
        this.buttons.SetButtonState(button, true);
        return true;
    }

    public bool KeyUp(Keys key)
    {
        if (!TryMap(key, out GameBoyButton button)) return false;
        this.buttons.SetButtonState(button, false);
        return true;
    }

    public void ReleaseAll() => this.buttons.ReleaseAll();

    private static bool TryMap(Keys key, out GameBoyButton button)
    {
        button = (key & Keys.KeyCode) switch
        {
            Keys.Right => GameBoyButton.Right,
            Keys.Left => GameBoyButton.Left,
            Keys.Up => GameBoyButton.Up,
            Keys.Down => GameBoyButton.Down,
            Keys.Z => GameBoyButton.A,
            Keys.X => GameBoyButton.B,
            Keys.Space => GameBoyButton.Select,
            Keys.Enter => GameBoyButton.Start,
            _ => 0
        };
        return button != 0;
    }
}
