namespace Portway.Core;
// 비밀 정보가 포함된 모든 필드는 저장 및 공개 API 응답 시 이 공통 변환을 거칩니다.
public static class SiteSecrets
{
    public static Site Map(Site site, Func<string, string?> transform) => site with
    {
        Password = site.Password == null ? null : transform(site.Password),
        Passphrase = site.Passphrase == null ? null : transform(site.Passphrase),
        SessionToken = site.SessionToken == null ? null : transform(site.SessionToken),
        EncryptionKey = site.EncryptionKey == null ? null : transform(site.EncryptionKey),
        ClientCertificatePassword = site.ClientCertificatePassword == null ? null : transform(site.ClientCertificatePassword),
        Proxy = site.Proxy with
        {
            Password = site.Proxy.Password == null ? null : transform(site.Proxy.Password)
        },
        Jump = site.Jump == null ? null : Map(site.Jump, transform)
    };
    public static bool HasAny(Site site) => site.Password != null || site.Passphrase != null || site.SessionToken != null || site.EncryptionKey != null || site.ClientCertificatePassword != null || site.Proxy.Password != null || site.Jump != null && HasAny(site.Jump);
    public static Site Public(Site site) => Map(site, _ => null) with
    {
        HasPassword = site.HasPassword || HasAny(site),
        EncryptFiles = site.EncryptFiles || site.EncryptionKey != null
    };
    public static bool SameEndpoint(Site a, Site b) => a.Host == b.Host && a.Username == b.Username && a.Port == b.Port && a.Protocol == b.Protocol && a.Bucket == b.Bucket && (a.EncryptFiles || a.EncryptionKey != null) == (b.EncryptFiles || b.EncryptionKey != null);
    public static Site Merge(Site current, Site saved, Func<string, string> decrypt)
    {
        if (!SameEndpoint(current, saved))
            return current;
        string? Value(string? a, string? b) => a ?? (b == null ? null : decrypt(b));
        return current with
        {
            Password = Value(current.Password, saved.Password),
            Passphrase = Value(current.Passphrase, saved.Passphrase),
            SessionToken = Value(current.SessionToken, saved.SessionToken),
            ClientCertificatePassword = Value(current.ClientCertificatePassword, saved.ClientCertificatePassword),
            EncryptionKey = Value(current.EncryptionKey, saved.EncryptionKey),
            Proxy = current.Proxy with
            {
                Password = current.Proxy.Host == saved.Proxy.Host && current.Proxy.Port == saved.Proxy.Port && current.Proxy.Username == saved.Proxy.Username && current.Proxy.Type == saved.Proxy.Type ? Value(current.Proxy.Password, saved.Proxy.Password) : current.Proxy.Password
            },
            Jump = current.Jump == null || saved.Jump == null ? current.Jump : Merge(current.Jump, saved.Jump, decrypt)
        };
    }
}
