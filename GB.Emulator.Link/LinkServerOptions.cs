using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GB.Emulator.Link;

public sealed record LinkServerOptions(X509Certificate2? Certificate = null, string? AccessToken = null,
    X509Certificate2Collection? CertificateChain = null)
{
    internal bool Accepts(string? supplied) => AccessToken == null || supplied != null && supplied.Length <= 256 &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(AccessToken)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    internal SslServerAuthenticationOptions? Authentication()
    {
        if (Certificate == null) return null;
        if (!Certificate.HasPrivateKey) throw new ArgumentException("The link server TLS certificate needs its private key.");
        return new() { ServerCertificateContext = SslStreamCertificateContext.Create(Certificate, CertificateChain),
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 };
    }
}
