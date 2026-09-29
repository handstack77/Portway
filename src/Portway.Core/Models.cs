namespace Portway.Core;

public sealed record Site
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "새 사이트";
    public string Protocol { get; init; } = "sftp";
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Username { get; init; } = "";
    public string? Password { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? Passphrase { get; init; }
    public string? Fingerprint { get; init; }
    public string RemotePath { get; init; } = "/";
    public string? LocalPath { get; init; }
    public string Bucket { get; init; } = "";
    public string Region { get; init; } = "us-east-1";
    public bool SavePassword { get; init; }
    public bool HasPassword { get; init; }
    public string Authentication { get; init; } = "automatic";
    public string? CertificatePath { get; init; }
    public string? AgentSocket { get; init; }
    public ProxySettings Proxy { get; init; } = new();
    public Site? Jump { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int KeepAliveSeconds { get; init; } = 20;
    public string FilenameEncoding { get; init; } = "utf-8";
    public string FtpDataMode { get; init; } = "passive";
    public string? TlsFingerprint { get; init; }
    public string? ClientCertificatePath { get; init; }
    public string? ClientCertificatePassword { get; init; }
    public string? SessionToken { get; init; }
    public string? EncryptionKey { get; init; }
    public bool EncryptFiles { get; init; }
    public bool S3PathStyle { get; init; } = true;
    public string S3StorageClass { get; init; } = "STANDARD";
    public bool S3RequesterPays { get; init; }
    public string Note { get; init; } = "";
    public string Color { get; init; } = "#15977c";
    public int EffectivePort => Port > 0 ? Port : Protocol switch
    {
        "sftp" or "scp" => 22,
        "ftp" or "ftps" => 21,
        "ftps-implicit" => 990,
        "webdav" => 80,
        _ => 443
    };

    public void Validate() => Validate(requireSecrets: true);
    public void Validate(bool requireSecrets)
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ArgumentException("호스트를 입력하세요.");
        if (Port is < 0 or > 65535)
            throw new ArgumentException("포트 범위는 1–65535입니다.");
        if (Protocol is not ("sftp" or "scp" or "ftp" or "ftps" or "ftps-implicit" or "webdav" or "webdavs" or "s3"))
            throw new ArgumentException("지원하지 않는 프로토콜입니다.");
        if (Protocol == "s3" && string.IsNullOrWhiteSpace(Bucket))
            throw new ArgumentException("S3 버킷을 입력하세요.");
        if (TimeoutSeconds is < 1 or > 3600 || KeepAliveSeconds is < 0 or > 3600)
            throw new ArgumentException("연결 시간 설정 범위를 확인하세요.");
        if (Authentication is not ("automatic" or "password" or "key" or "agent" or "pageant" or "keyboard"))
            throw new ArgumentException("인증 방식이 올바르지 않습니다.");
        Proxy.Validate();
        if (requireSecrets && EncryptFiles && EncryptionKey == null)
            throw new ArgumentException("파일 암호화 키가 필요합니다. Vault를 잠금 해제하거나 키를 입력하세요.");
        if (EncryptionKey != null)
        {
            if (Protocol != "sftp")
                throw new ArgumentException("파일 암호화는 SFTP에서 사용하세요.");
            if (EncryptionKey.Length != 64 || !EncryptionKey.All(Uri.IsHexDigit))
                throw new ArgumentException("파일 암호화 키는 64자리 16진수입니다.");
        }

        if (Jump != null)
        {
            if (Jump.Jump != null || Jump.Protocol is not ("sftp" or "scp"))
                throw new ArgumentException("점프 서버는 단일 SSH 연결이어야 합니다.");
            if (Protocol is not ("sftp" or "scp"))
                throw new ArgumentException("SSH 터널은 SFTP/SCP에서 사용하세요.");
            Jump.Validate(requireSecrets);
        }
    }
}

public sealed record ProxySettings(string Type = "none", string Host = "", int Port = 8080, string Username = "", string? Password = null)
{
    public void Validate()
    {
        if (Type is not ("none" or "http" or "socks4" or "socks5"))
            throw new ArgumentException("프록시 종류가 올바르지 않습니다.");
        if (Type != "none" && (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535))
            throw new ArgumentException("프록시 주소와 포트가 필요합니다.");
    }
}

public record AuthenticationPrompt(string Text, bool Echo);
public interface IAuthenticationInteraction
{
    Task<string[]> Answer(Site site, string instruction, AuthenticationPrompt[] prompts, CancellationToken ct);
}

public record Entry(string Name, string Path, bool IsDirectory, long Size, DateTimeOffset Modified, string Permissions = "", bool IsLink = false, string? Owner = null, string? Group = null);
public record Listing(string Path, string? Parent, Entry[] Entries);
public record TransferProgress(long Bytes, long Total);
public record Capabilities(bool Resume, bool Permissions, bool Commands, bool AtomicRename, bool Timestamps = false, bool Links = false);
public record FileRequest(string Path, string? Destination = null, string? Content = null, string? Etag = null, string? Encoding = null, bool? Bom = null);
public record TransferRequest(string SessionId, string Direction, string[] Paths, string Destination, string Conflict = "skip", int SpeedLimit = 0, TransferOptions? Options = null, int Priority = 0, DateTimeOffset? ScheduledAt = null);
public record SyncRequest(string SessionId, string LocalPath, string RemotePath, string Direction = "upload", bool DeleteExtraneous = false, TransferOptions? Options = null, string Comparison = "checksum");
public record Bookmark(string Name, string LocalPath, string RemotePath);
public record Preferences(string UpdateUrl = "", List<Bookmark>? Bookmarks = null, TransferOptions? TransferDefaults = null, int MaxConcurrent = 2, string Theme = "system", string View = "commander", List<TransferPreset>? Presets = null, string EditorExecutable = "", string[]? EditorArguments = null, List<CustomCommand>? Commands = null);
public record CustomCommand(string Name, string Template, bool Remote = true);
public record TransferPreset(string Name, TransferOptions Options, string HostPattern = "*");
public interface IRemoteFileSystem : IAsyncDisposable
{
    Capabilities Capabilities { get; }

    Task Connect(CancellationToken ct);
    Task<Entry[]> List(string path, CancellationToken ct);
    Task<Entry?> Stat(string path, CancellationToken ct);
    Task CreateDirectory(string path, CancellationToken ct);
    Task Delete(string path, bool directory, CancellationToken ct);
    Task Move(string source, string destination, CancellationToken ct);
    Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct);
    Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct);
    Task Chmod(string path, string octal, CancellationToken ct) => throw new NotSupportedException("이 프로토콜은 권한 변경을 지원하지 않습니다.");
    Task SetModified(string path, DateTimeOffset modified, CancellationToken ct) => throw new NotSupportedException("이 서버는 수정 시각 변경을 지원하지 않습니다.");
    Task CreateLink(string target, string link, CancellationToken ct) => throw new NotSupportedException("이 프로토콜은 링크 생성을 지원하지 않습니다.");
    Task SetOwner(string path, int? owner, int? group, CancellationToken ct) => throw new NotSupportedException("이 프로토콜은 소유권 변경을 지원하지 않습니다.");
    Task Copy(string source, string destination, CancellationToken ct) => RemoteFileOperations.Copy(this, source, destination, ct);
    Task<string> Command(string command, CancellationToken ct) => throw new NotSupportedException("이 프로토콜은 셸 명령을 지원하지 않습니다.");
}
