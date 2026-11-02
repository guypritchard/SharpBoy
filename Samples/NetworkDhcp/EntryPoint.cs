using GB.Sdk;

static class DhcpEntryPoint
{
    [GameEntryPoint]
    public static void Start() { NetworkDhcp.Run(); }
}
