namespace GB.Emulator.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.FirstOrDefault() == "--desktop")
        {
            try { new System.Windows.Application().Run(new Desktop.DesktopWindow(args.Skip(1).ToArray())); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Could not start the desktop Game Boy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }
        Application.Run(new PlayerForm(args.FirstOrDefault()));
    }
}
