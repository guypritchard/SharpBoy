using GB.Sdk;

static class NetworkTelemetry
{
    [GameEntryPoint]
    public static void Start()
    {
        NetworkDhcp.Run();
        GameNetwork.Write8(0xC060, 0);
        if (GameNetwork.Read8(0xC000) != 1 || !GameNetwork.OpenConfigured()) { GameNetwork.Write8(0xC060, 255); return; }
        // GBT1, node id, transport, payload length (LE), then six raw network-order values.
        GameNetwork.Write8(0xC070, 71); GameNetwork.Write8(0xC071, 66);
        GameNetwork.Write8(0xC072, 84); GameNetwork.Write8(0xC073, 49);
        GameNetwork.Write8(0xC074, GameNetwork.Read8(0xA030));
        GameNetwork.Write8(0xC075, GameNetwork.Read8(0xA031));
        GameNetwork.Write8(0xC076, 24); GameNetwork.Write8(0xC077, 0);
        GameNetwork.CopyBytes(0xC010, 0xC078, 24);
        if (GameNetwork.Send(0xC070, 32) && GameNetwork.Execute(4) == 0) GameNetwork.Write8(0xC060, 1);
        else GameNetwork.Write8(0xC060, 255);
    }
}
