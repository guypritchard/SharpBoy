using GB.Experiments.NetworkCartridge;
using GB.Experiments.Windows.Labs;

namespace GB.Experiments.Windows;

internal enum ExperimentMode { Launcher, Dhcp, Fractal, Life }
internal sealed record ExperimentLaunch(ExperimentMode Mode, string[] Arguments, string? Adapter = null)
{
    internal static ExperimentLaunch Parse(string[] args)
    {
        if (args.Length == 0) return new(ExperimentMode.Launcher, []);
        return args[0] switch
        {
            "--life" => new(ExperimentMode.Life, args[1..]),
            "--cluster" => new(ExperimentMode.Fractal, args[1..]),
            "--dhcp" when args.Length == 1 => new(ExperimentMode.Dhcp, []),
            "--dhcp" when args is [_, "--adapter", var adapter] && !string.IsNullOrWhiteSpace(adapter) => new(ExperimentMode.Dhcp, [], adapter),
            _ => throw new ArgumentException("Usage: GB.Experiments.Windows [--life options | --cluster options | --dhcp [--adapter NAME]]")
        };
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var launch = ExperimentLaunch.Parse(args);
            switch (launch.Mode)
            {
                case ExperimentMode.Launcher: Application.Run(new ExperimentLauncherForm()); break;
                case ExperimentMode.Life: new System.Windows.Application().Run(new LifeClusterWindow(launch.Arguments)); break;
                case ExperimentMode.Fractal: new System.Windows.Application().Run(new FractalClusterWindow(launch.Arguments)); break;
                case ExperimentMode.Dhcp:
                    Application.Run(launch.Adapter is { } adapter
                        ? new DhcpExperimentForm(() => new EthernetDhcpBackend(new NpcapEthernetLink(adapter)), "Ethernet: " + adapter)
                        : new DhcpExperimentForm());
                    break;
            }
        }
        catch (Exception ex)
        { MessageBox.Show(ex.Message, "Could not start the experiment", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
