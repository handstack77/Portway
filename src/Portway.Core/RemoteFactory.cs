using Portway.Core.Protocols;

namespace Portway.Core;

/// <summary>사이트 설정을 검증하고 프로토콜·암호화·터널·경로 보호 구현을 조합합니다.</summary>
public sealed class RemoteFactory(IAuthenticationInteraction? interaction = null)
{
    public IRemoteFileSystem Create(Site site)
    {
        site.Validate();
        if (site.EncryptionKey != null)
            return new GuardedRemote(new EncryptedFileSystem(Create(site with { EncryptionKey = null, EncryptFiles = false }), site.EncryptionKey));
        if (site.Jump != null)
            return new GuardedRemote(new TunneledFileSystem(site, interaction, Create));
        IRemoteFileSystem implementation = site.Protocol switch
        {
            "sftp" => new SftpFileSystem(site, interaction),
            "scp" => new ScpFileSystem(site, interaction),
            "ftp" or "ftps" or "ftps-implicit" => new FtpFileSystem(site),
            "webdav" or "webdavs" => new WebDavFileSystem(site),
            "s3" => new S3FileSystem(site),
            _ => throw new ArgumentException("지원하지 않는 프로토콜입니다.")
        };
        return new GuardedRemote(implementation);
    }
}
