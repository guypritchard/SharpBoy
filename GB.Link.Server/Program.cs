using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using GB.Emulator.Link;
using GB.Link.Server;

var address = IPAddress.Parse(Environment.GetEnvironmentVariable("LINK_BIND") ?? "127.0.0.1");
int port = int.Parse(Environment.GetEnvironmentVariable("LINK_PORT") ?? Environment.GetEnvironmentVariable("PORT") ?? "8766");
bool healthCheck = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--health-check") { healthCheck = true; continue; }
    if (i + 1 == args.Length) throw new ArgumentException("Usage: GB.Link.Server [--bind 0.0.0.0] [--port 8766]");
    if (args[i] == "--bind") address = IPAddress.Parse(args[++i]);
    else if (args[i] == "--port" && int.TryParse(args[++i], out int number) && number is >= 1 and <= 65535) port = number;
    else throw new ArgumentException("Usage: GB.Link.Server [--bind 0.0.0.0] [--port 8766]");
}
if (port is < 1 or > 65535) throw new ArgumentException("The link port must be from 1 to 65535.");
string? Secret(string name)
{
    string? value = Environment.GetEnvironmentVariable(name), path = Environment.GetEnvironmentVariable(name + "_FILE");
    if (value != null && path != null) throw new ArgumentException($"Specify {name} or {name}_FILE, not both.");
    if (path != null)
    {
        if (new FileInfo(path).Length > 4096) throw new ArgumentException("Secret file is too large.");
        value = File.ReadAllText(path).TrimEnd('\r', '\n');
    }
    return value;
}
string? token = Secret("LINK_ACCESS_TOKEN");
if (token != null && (token.Length is < 1 or > 256 || token.Any(char.IsControl))) throw new ArgumentException("Join tokens must contain 1-256 printable characters.");
var certificates = new X509Certificate2Collection();
var certificatePath = Environment.GetEnvironmentVariable("LINK_TLS_PFX");
try
{
        if (certificatePath != null) certificates = X509CertificateLoader.LoadPkcs12CollectionFromFile(certificatePath, Secret("LINK_TLS_PASSWORD"),
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    var certificate = certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey);
    if (certificatePath != null && certificate == null) throw new ArgumentException("The PFX must contain a server certificate and private key.");
    if (healthCheck)
    {
        try { return await LinkLobbyProbe.CheckAsync("127.0.0.1", port, certificate != null, token, certificate) ? 0 : 1; }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException) { return 1; }
    }
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Cancel(); });
await using var server = new LinkLobbyServer(address, port, new(certificate, token, certificates));
        server.ClientError += ex => Console.Error.WriteLine($"Client connection rejected: {ex.Message}");
string? botRom = Environment.GetEnvironmentVariable("LINK_TETRIS_ROM");
int botCount = int.Parse(Environment.GetEnvironmentVariable("LINK_TETRIS_PLAYERS") ?? "4");
await using var players = botRom == null ? null : new TetrisPlayerPool(server, File.ReadAllBytes(botRom), botCount,
    Environment.GetEnvironmentVariable("LINK_BOT_HOST") ?? (address.Equals(IPAddress.Any) ? "127.0.0.1" : address.Equals(IPAddress.IPv6Any) ? "::1" : address.ToString()),
    certificate != null, token, Console.WriteLine);
if (players != null) await players.Ready.WaitAsync(stop.Token);
Console.WriteLine($"SharpBoy link hub listening on {address}:{server.Port}; TLS={(certificate != null ? "on" : "off")}; join-token={(token != null ? "required" : "off")}; headless players={(players == null ? 0 : botCount)}. ROM data is never sent to clients.");
try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
Console.WriteLine("Stopping lobby and draining all cable connections.");
return 0;
}
finally { foreach (var certificate in certificates) certificate.Dispose(); }
