using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GB.Emulator.Link;

public static class LinkLobbyProbe
{
    /// <summary>Protocol readiness without registering a player. Optional pin is for the container's own configured certificate.</summary>
    public static async Task<bool> CheckAsync(string host, int port, bool useTls = false, string? accessToken = null,
        X509Certificate2? expectedCertificate = null, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var socket = new TcpClient(); await socket.ConnectAsync(host, port, timeout.Token);
        using var tls = useTls ? new SslStream(socket.GetStream(), false, expectedCertificate == null ? null :
            (_, cert, _, _) => cert != null && CryptographicOperations.FixedTimeEquals(cert.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256), expectedCertificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256))) : null;
        Stream stream = tls == null ? socket.GetStream() : tls;
        if (tls != null) await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);
        await LinkProtocol.WriteAsync(stream, new("probe", AccessToken: accessToken), timeout.Token);
        return (await LinkProtocol.ReadAsync(stream, timeout.Token))?.Kind == "ok";
    }
}
