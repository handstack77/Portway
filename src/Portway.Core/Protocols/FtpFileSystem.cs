using FluentFTP;
using FluentFTP.Proxy.AsyncProxy;
using System.Net;
using System.Text;

namespace Portway.Core.Protocols;

public sealed class FtpFileSystem : IRemoteFileSystem
{
    readonly AsyncFtpClient client;
    public FtpFileSystem(Site site)
    {
        var proxy = new FtpProxyProfile
        {
            ProxyHost = site.Proxy.Host,
            ProxyPort = site.Proxy.Port,
            ProxyCredentials = new NetworkCredential(site.Proxy.Username, site.Proxy.Password),
            FtpHost = site.Host,
            FtpPort = site.EffectivePort,
            FtpCredentials = new NetworkCredential(site.Username, site.Password)
        };
        client = site.Proxy.Type switch
        {
            "http" => new AsyncFtpClientHttp11Proxy(proxy),
            "socks4" => new AsyncFtpClientSocks4aProxy(proxy),
            "socks5" => new AsyncFtpClientSocks5Proxy(proxy),
            _ => new AsyncFtpClient(site.Host, site.Username, site.Password ?? "", site.EffectivePort)
        };
        client.Config.EncryptionMode = site.Protocol switch
        {
            "ftps" => FtpEncryptionMode.Explicit,
            "ftps-implicit" => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None
        };
        client.Config.ValidateAnyCertificate = false;
        client.ValidateCertificate += (_, e) => e.Accept = TlsOptions.Accept(e.Certificate, e.PolicyErrors, site.TlsFingerprint);
        if (TlsOptions.ClientCertificate(site) is { } cert)
            client.Config.ClientCertificates.Add(cert);
        client.Config.DataConnectionType = site.FtpDataMode switch
        {
            "active" => FtpDataConnectionType.AutoActive,
            "pasv" => FtpDataConnectionType.PASV,
            "epsv" => FtpDataConnectionType.EPSV,
            "passive" => FtpDataConnectionType.AutoPassive,
            _ => throw new ArgumentException("FTP 데이터 연결 모드가 올바르지 않습니다.")
        };
        client.Encoding = TextCodec.GetEncoding(site.FilenameEncoding);
        client.Config.ConnectTimeout = site.TimeoutSeconds * 1000;
        client.Config.ReadTimeout = site.TimeoutSeconds * 1000;
        client.Config.DataConnectionConnectTimeout = site.TimeoutSeconds * 1000;
        client.Config.DataConnectionReadTimeout = site.TimeoutSeconds * 1000;
    }

    public Capabilities Capabilities => new(true, true, false, true, client.HasFeature(FtpCapability.MFMT));

    public Task Connect(CancellationToken ct) => client.Connect(ct);
    static Entry Map(FtpListItem f) => new(f.Name, f.FullName, f.Type == FtpObjectType.Directory, Math.Max(0, f.Size), new DateTimeOffset(DateTime.SpecifyKind(f.Modified, DateTimeKind.Utc)), f.Chmod == 0 ? "" : f.Chmod.ToString(), f.Type == FtpObjectType.Link);
    public async Task<Entry[]> List(string path, CancellationToken ct) => (await client.GetListing(path, FtpListOption.Modify | FtpListOption.Size, ct)).Where(f => f.Name is not "." and not "..").Select(Map).ToArray();
    public async Task<Entry?> Stat(string path, CancellationToken ct)
    {
        var f = await client.GetObjectInfo(path, token: ct);
        return f == null ? null : Map(f);
    }

    public async Task CreateDirectory(string path, CancellationToken ct) => await client.CreateDirectory(path, ct);
    public Task Delete(string path, bool directory, CancellationToken ct) => directory ? client.DeleteDirectory(path, ct) : client.DeleteFile(path, ct);
    public Task Move(string source, string destination, CancellationToken ct) => client.Rename(source, destination, ct);
    public async Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        var size = await client.GetFileSize(remote, -1, ct);
        var result = await client.DownloadFile(local, remote, offset > 0 ? FtpLocalExists.Resume : FtpLocalExists.Overwrite, FtpVerify.None, new InlineProgress<FtpProgress>(p => progress(new((long)p.TransferredBytes, size))), ct);
        if (result == FtpStatus.Failed)
            throw new IOException("FTP 다운로드가 실패했습니다.");
    }

    public async Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        var size = new FileInfo(local).Length;
        var result = await client.UploadFile(local, remote, offset > 0 ? FtpRemoteExists.Resume : FtpRemoteExists.Overwrite, false, FtpVerify.None, new InlineProgress<FtpProgress>(p => progress(new((long)p.TransferredBytes, size))), ct);
        if (result == FtpStatus.Failed)
            throw new IOException("FTP 업로드가 실패했습니다.");
    }

    public Task Chmod(string path, string octal, CancellationToken ct) => client.Chmod(path, int.Parse(octal), ct);
    public Task SetModified(string path, DateTimeOffset modified, CancellationToken ct) => client.SetModifiedTime(path, modified.UtcDateTime, ct);
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (client.IsConnected)
                await client.Disconnect();
        }
        finally
        {
            client.Dispose();
        }
    }

    sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
