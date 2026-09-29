using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Portway.Core.Protocols;

public static class TlsOptions
{
    public static string Fingerprint(X509Certificate certificate) => "SHA256:" + certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public static bool Accept(X509Certificate? certificate, SslPolicyErrors errors, string? pin)
    {
        if (certificate == null)
            return false;
        if (string.IsNullOrWhiteSpace(pin))
            return errors == SslPolicyErrors.None;
        var expected = pin.Replace("SHA256:", "", StringComparison.OrdinalIgnoreCase).Replace(":", "").Replace(" ", "");
        return expected.Length == 64 && string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), expected, StringComparison.OrdinalIgnoreCase);
    }

    public static X509Certificate2? ClientCertificate(Site site) => string.IsNullOrWhiteSpace(site.ClientCertificatePath) ? null : X509CertificateLoader.LoadPkcs12FromFile(site.ClientCertificatePath, site.ClientCertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
    public static HttpClientHandler HttpHandler(Site site)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            Credentials = new NetworkCredential(site.Username, site.Password),
            PreAuthenticate = true,
            UseProxy = site.Proxy.Type != "none"
        };
        if (handler.UseProxy)
            handler.Proxy = new WebProxy(new Uri($"{site.Proxy.Type}://{site.Proxy.Host}:{site.Proxy.Port}"))
            {
                Credentials = new NetworkCredential(site.Proxy.Username, site.Proxy.Password)
            };
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) => Accept(cert, errors, site.TlsFingerprint);
        if (ClientCertificate(site) is { } certificate)
            handler.ClientCertificates.Add(certificate);
        return handler;
    }

    public static async Task<object> Probe(Site site, CancellationToken ct)
    {
        if (site.Protocol is not ("ftps" or "ftps-implicit" or "webdavs"))
            throw new ArgumentException("FTPS 또는 HTTPS 연결에서 인증서를 확인하세요.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(site.TimeoutSeconds));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(site.Host, site.EffectivePort, timeout.Token);
        var network = tcp.GetStream();
        if (site.Protocol == "ftps")
        {
            async Task<string> Line()
            {
                var bytes = new List<byte>();
                var one = new byte[1];
                while (bytes.Count < 8192 && await network.ReadAsync(one, timeout.Token) > 0)
                {
                    bytes.Add(one[0]);
                    if (one[0] == 10)
                        return Encoding.ASCII.GetString(bytes.ToArray());
                }

                throw new IOException("FTP 응답을 읽을 수 없습니다.");
            }

            var welcome = await Line();
            if (welcome.StartsWith("220-"))
                do
                {
                    welcome = await Line();
                }
                while (!welcome.StartsWith("220 "));
            if (!welcome.StartsWith("220"))
                throw new IOException("FTP 서버가 준비되지 않았습니다.");
            await network.WriteAsync("AUTH TLS\r\n"u8.ToArray(), timeout.Token);
            if (!(await Line()).StartsWith("234"))
                throw new IOException("서버가 AUTH TLS를 거부했습니다.");
        }

        X509Certificate2? captured = null;
        using var ssl = new SslStream(network, false, (_, certificate, _, _) =>
        {
            if (certificate != null)
                captured = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            return false;
        });
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = site.Host }, timeout.Token);
        }
        catch when (captured != null && !timeout.IsCancellationRequested)
        {
        }

        using var result = captured ?? throw new IOException("서버 인증서를 읽지 못했습니다.");
        return new
        {
            fingerprint = Fingerprint(result),
            subject = result.Subject,
            issuer = result.Issuer,
            notBefore = result.NotBefore,
            notAfter = result.NotAfter
        };
    }
}
