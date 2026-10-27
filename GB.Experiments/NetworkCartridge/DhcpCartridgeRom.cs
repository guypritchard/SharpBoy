namespace GB.Experiments.NetworkCartridge;

/// <summary>SDK-compiled native DHCP firmware. Rebuild from Samples/NetworkDhcp with its build script.</summary>
public static class DhcpCartridgeRom
{
    public const ushort Result = 0xC000, State = 0xC001, Retry = 0xC002, Noise = 0xC003, NakBudget = 0xC005;
    public const ushort Transaction = 0xC008, Address = 0xC010, Server = 0xC014, Mask = 0xC018;
    public const ushort Router = 0xC01C, Dns = 0xC020, Lease = 0xC024;
    public const ushort ReceiveBuffer = NetworkCartridgeDevice.TxBuffer + NetworkCartridgeDevice.DatagramCapacity;

    public static byte[] Create()
    {
        using var stream = typeof(DhcpCartridgeRom).Assembly.GetManifestResourceStream("GB.Experiments.NetworkDhcp.gb")
            ?? throw new InvalidOperationException("The SDK-built DHCP firmware resource is missing.");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
