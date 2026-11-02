namespace GB.Emulator.Core.InputOutput
{
    public interface IButtonInput
    {
        void SetButtonState(GameBoyButton button, bool pressed);
        bool IsPressed(GameBoyButton button);
        void ReleaseAll();
    }
}
