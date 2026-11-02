using GB.Emulator.Hosting;

namespace GB.Experiments.NetworkCartridge;

/// <summary>Detached inspection captured by the machine owner, never by its presentation thread.</summary>
public sealed record NativeNodeSnapshot(ushort NodeId, string State, double WallSeconds, long Cycles,
    double CyclesPerSecond, CpuSnapshot Cpu, HardwareSnapshot Hardware, CartridgeSnapshot Cartridge,
    int NonZeroWorkRamBytes, LocalNetworkTransport Transport, TcpTransportSnapshot Tcp,
    byte NetworkStatus, NetworkError NetworkError, int NetworkBufferBytes, string? HostName = null, int ProcessId = 0)
{
    public const int NominalClockHz = 4_194_304;
    public double Speed => CyclesPerSecond / NominalClockHz;
}
