using System;

namespace GB.Debugger;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DebuggerForm());
    }
}

