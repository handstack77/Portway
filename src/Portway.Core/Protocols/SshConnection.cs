using System.Security.Cryptography;
using Renci.SshNet;
using SshNet.Agent;
using System.Text;

namespace Portway.Core.Protocols;

public static class SshConnection
{
    static SshConnection() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static ConnectionInfo Info(Site site, IAuthenticationInteraction? interaction = null, bool probe = false)
    {
        var methods = new List<AuthenticationMethod>();
        var username = string.IsNullOrWhiteSpace(site.Username) && probe ? "probe" : site.Username;
        if (probe)
            methods.Add(new NoneAuthenticationMethod(username));
        else
        {
            if (site.Authentication is "agent" or "pageant")
            {
                if (site.Authentication == "pageant" && !OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("이 OS에서는 OpenSSH Agent를 선택하세요.");
                var agent = site.Authentication == "pageant" ? new Pageant() : string.IsNullOrEmpty(site.AgentSocket) ? new SshAgent() : new SshAgent(site.AgentSocket, TimeSpan.FromSeconds(site.TimeoutSeconds));
                var keys = agent.RequestIdentities().Cast<IPrivateKeySource>().ToArray();
                if (keys.Length == 0)
                    throw new InvalidOperationException("Agent에 등록된 키가 없습니다.");
                methods.Add(new PrivateKeyAuthenticationMethod(username, keys));
            }

            if (site.Authentication is "automatic" or "key" && !string.IsNullOrWhiteSpace(site.PrivateKeyPath))
                methods.Add(new PrivateKeyAuthenticationMethod(username, site.CertificatePath == null ? new PrivateKeyFile(site.PrivateKeyPath, site.Passphrase ?? "") : new PrivateKeyFile(site.PrivateKeyPath, site.Passphrase, site.CertificatePath)));
            if (site.Authentication == "keyboard")
            {
                if (interaction == null)
                    throw new InvalidOperationException("대화형 인증 응답기가 필요합니다.");
                var keyboard = new KeyboardInteractiveAuthenticationMethod(username);
                keyboard.AuthenticationPrompt += (_, e) =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    var prompts = e.Prompts.ToArray();
                    var answers = interaction.Answer(site, e.Instruction, prompts.Select(p => new AuthenticationPrompt(p.Request, p.IsEchoed)).ToArray(), timeout.Token).GetAwaiter().GetResult();
                    if (answers.Length != prompts.Length)
                        throw new IOException("인증 응답 수가 올바르지 않습니다.");
                    for (var i = 0; i < prompts.Length; i++)
                        prompts[i].Response = answers[i];
                };
                methods.Add(keyboard);
            }

            if (site.Authentication == "password" || site.Authentication == "automatic" && (site.Password != null || methods.Count == 0))
                methods.Add(new PasswordAuthenticationMethod(username, site.Password ?? ""));
            if (methods.Count == 0)
                throw new ArgumentException("인증에 필요한 키 또는 암호를 설정하세요.");
        }

        var proxy = Enum.Parse<ProxyTypes>(site.Proxy.Type, true);
        return new ConnectionInfo(site.Host, site.EffectivePort, username, proxy, site.Proxy.Host, site.Proxy.Port, site.Proxy.Username, site.Proxy.Password ?? "", methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(site.TimeoutSeconds),
            Encoding = TextCodec.GetEncoding(site.FilenameEncoding)
        };
    }

    public static string Fingerprint(byte[] key) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
    public static T Verify<T>(T client, Site site)
        where T : BaseClient
    {
        if (string.IsNullOrWhiteSpace(site.Fingerprint))
        {
            client.Dispose();
            throw new ArgumentException("연결 전에 SSH 호스트 키 지문을 확인하세요.");
        }

        client.HostKeyReceived += (_, e) => e.CanTrust = string.Equals(Fingerprint(e.HostKey), site.Fingerprint, StringComparison.Ordinal);
        client.KeepAliveInterval = site.KeepAliveSeconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(site.KeepAliveSeconds);
        return client;
    }

    public static async Task<string> Scan(Site site, CancellationToken ct, IAuthenticationInteraction? interaction = null)
    {
        // 인증 전에 중단합니다. 검증되지 않은 호스트에는 인증 정보를 보내지 않습니다.
        await using var route = await SshRoute.Open(site, interaction, ct);
        using var client = new SshClient(Info(route.Target, probe: true));
        string? fingerprint = null;
        client.HostKeyReceived += (_, e) =>
        {
            fingerprint = Fingerprint(e.HostKey);
            e.CanTrust = false;
        };
        try
        {
            await client.ConnectAsync(ct);
        }
        catch when (fingerprint != null && !ct.IsCancellationRequested)
        {
        }

        return fingerprint ?? throw new IOException("호스트 키를 읽지 못했습니다.");
    }
}
