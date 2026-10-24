namespace GB.Emulator.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PlayerForm(args.FirstOrDefault()));
    }
}
