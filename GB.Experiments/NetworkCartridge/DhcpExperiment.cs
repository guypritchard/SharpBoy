using System.Buffers.Binary;
using System.Net;
using GB.Emulator.Core;

namespace GB.Experiments.NetworkCartridge;

public sealed record DhcpExperimentResult(bool Acquired, byte State, uint Address, uint Server, uint Mask,
    uint Router, uint Dns, uint LeaseSeconds, long Cycles);

public static class DhcpExperiment
{
    public static async Task<DhcpExperimentResult> RunRomAsync(IDatagramCartridgeBackend backend, CancellationToken cancellationToken = default)
    {
        await using var session = new DhcpSession(backend);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (!session.IsComplete)
        {
            deadline.Token.ThrowIfCancellationRequested();
            session.Advance(TimeSpan.FromSeconds(1.0 / 60));
            if (session.HasPendingOperation) await Task.Delay(1, deadline.Token);
        }
        return session.CaptureResult();
    }

    public static string FormatAddress(uint address) => new IPAddress(DhcpPacket.Word(address)).ToString();

    public static async Task<int> RunAsync(string[] args)
    {
        bool dropOffer = false, dropAck = false, nak = false;
        string? output = null, adapter = null;
        if (args is ["--list-adapters"])
        {
            try
            {
                foreach (EthernetAdapter item in NpcapEthernetLink.ListAdapters()) Console.WriteLine($"{item.Name}\n  {item.Description}");
                return 0;
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or DllNotFoundException or BadImageFormatException)
            { Console.Error.WriteLine(ex.Message); return 1; }
        }
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--drop-first-offer") dropOffer = true;
            else if (args[i] == "--drop-first-ack") dropAck = true;
            else if (args[i] == "--nak-first-request") nak = true;
            else if (args[i] == "--rom-output" && i + 1 < args.Length) output = args[++i];
            else if (args[i] == "--adapter" && i + 1 < args.Length) adapter = args[++i];
            else { Console.Error.WriteLine("Usage: GB.Experiments --network-cartridge --dhcp [--adapter Npcap-name | --list-adapters] [--drop-first-offer] [--drop-first-ack] [--nak-first-request] [--rom-output demo.gb]"); return 2; }
        }
        if (adapter != null && (dropOffer || dropAck || nak))
        { Console.Error.WriteLine("Loss/NAK simulation flags apply only to the virtual network."); return 2; }
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (output != null)
            {
                string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, DhcpCartridgeRom.Create(), shutdown.Token);
            }
            VirtualDhcpNetwork? network = adapter == null ? new(new() { DropFirstOffer = dropOffer, DropFirstAck = dropAck, NakFirstRequest = nak }) : null;
            IDatagramCartridgeBackend backend = network != null ? network : new EthernetDhcpBackend(new NpcapEthernetLink(adapter!));
            Console.WriteLine(adapter == null ? "DHCP on a virtual UDP network" : $"DHCP through Ethernet adapter {adapter}");
            DhcpExperimentResult result = await RunRomAsync(backend, shutdown.Token);
            if (network != null) Console.WriteLine(string.Join(" → ", network.Transcript));
            Console.WriteLine($"ROM result: {(result.Acquired ? "BOUND" : "FAILED")}");
            if (result.Acquired)
                Console.WriteLine($"IP {FormatAddress(result.Address)}, mask {FormatAddress(result.Mask)}, gateway {FormatAddress(result.Router)}, DNS {FormatAddress(result.Dns)}, server {FormatAddress(result.Server)}, lease {result.LeaseSeconds}s");
            return result.Acquired ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("DHCP experiment cancelled or timed out."); return 1; }
        catch (IOException ex) { Console.Error.WriteLine(ex.Message); return 1; }
        catch (Exception ex) when (ex is PlatformNotSupportedException or ArgumentException or DllNotFoundException or BadImageFormatException)
        { Console.Error.WriteLine(ex.Message); return 1; }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
