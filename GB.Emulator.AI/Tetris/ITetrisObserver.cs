using GB.Emulator.Hosting;

namespace GB.Emulator.AI.Tetris;

/// <summary>Called on the emulator owner thread. Null means menus, transitions or unknown state.</summary>
public interface ITetrisObserver
{
    TetrisObservation? Observe(EmulationInspection inspection);
}
