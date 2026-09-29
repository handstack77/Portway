using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public sealed class SiteArchiveTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-site-archive-" + Guid.NewGuid().ToString("N"));
    ProfileStore Store() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = root }).Build());
    [Theory]
    [InlineData("sftp")]
    [InlineData("scp")]
    [InlineData("ftp")]
    [InlineData("ftps")]
    [InlineData("ftps-implicit")]
    [InlineData("webdav")]
    [InlineData("webdavs")]
    [InlineData("s3")]
    public void ProtocolAndAdvancedMetadataSurviveRoundTrip(string protocol)
    {
        var original = new Site
        {
            Name = "한글 사이트 / %",
            Host = "example.com",
            Protocol = protocol,
            Port = 2222,
            Username = "user",
            Bucket = "bucket",
            Region = "ap-northeast-2",
            PrivateKeyPath = "/keys/한글.key",
            LocalPath = "/local/한글",
            RemotePath = "/remote/한글",
            Fingerprint = "SHA256:server",
            TlsFingerprint = "SHA256:tls",
            Proxy = new("socks5", "proxy.example", 1080, "proxy-user"),
            Authentication = "key",
            TimeoutSeconds = 75,
            FtpDataMode = "active",
            S3StorageClass = "GLACIER",
            Note = "메모",
            Color = "#066fd1"
        };
        var imported = Assert.Single(SiteArchive.Import(SiteArchive.Export([original])).Sites);
        Assert.Equal(original, imported);
        imported.Validate();
    }

    [Fact]
    public void SecretsAreRemovedRecursivelyAndEncryptionStaysRequired()
    {
        var secret = new Site
        {
            Host = "ssh.example",
            Password = "SECRET-password",
            Passphrase = "SECRET-passphrase",
            SessionToken = "SECRET-token",
            EncryptionKey = new string('a', 64),
            ClientCertificatePassword = "SECRET-certificate",
            SavePassword = true,
            HasPassword = true,
            Proxy = new("http", "proxy.example", 8080, "user", "SECRET-proxy"),
            Jump = new Site
            {
                Host = "jump.example",
                Password = "SECRET-jump",
                SavePassword = true,
                HasPassword = true
            }
        };
        var content = SiteArchive.Export([secret]);
        Assert.DoesNotContain("SECRET-", content);
        Assert.DoesNotContain(secret.EncryptionKey!, content);
        var imported = Assert.Single(SiteArchive.Import(content).Sites);
        Assert.True(imported.EncryptFiles);
        Assert.Null(imported.EncryptionKey);
        Assert.False(imported.SavePassword);
        Assert.False(imported.HasPassword);
        Assert.Null(imported.Jump!.Password);
        Assert.False(imported.Jump.SavePassword);
        Assert.False(imported.Jump.HasPassword);
        imported.Validate(requireSecrets: false);
        Assert.Throws<ArgumentException>(() => imported.Validate());
        (imported with
        {
            EncryptionKey = secret.EncryptionKey
        }

        ).Validate();
        var untrusted = JsonSerializer.Serialize(new SiteArchive.Document("portway-sites", 1, [secret]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("SECRET-", SiteArchive.Export(SiteArchive.Import(untrusted).Sites));
    }

    [Fact]
    public void ImportAddsNewIdsWithoutChangingSavedVaultSecrets()
    {
        using var profiles = Store();
        profiles.Unlock("site-archive-test-master-password");
        var original = new Site
        {
            Host = "ssh.example",
            Password = "SECRET-original",
            SavePassword = true
        };
        profiles.Save(original);
        var archive = SiteArchive.Export(profiles.List());
        profiles.Lock();
        var imported = SiteArchive.Import(archive);
        Assert.Equal(1, profiles.ImportSites(imported.Sites));
        Assert.Equal(1, profiles.ImportSites(imported.Sites));
        var saved = profiles.List();
        Assert.Equal(3, saved.Length);
        Assert.Equal(3, saved.Select(s => s.Id).Distinct().Count());
        Assert.All(saved.Where(s => s.Id != original.Id), s =>
        {
            Assert.False(s.SavePassword);
            Assert.False(s.HasPassword);
        });
        profiles.Unlock("site-archive-test-master-password");
        Assert.Equal(original.Password, profiles.Hydrate(saved.Single(s => s.Id == original.Id)).Password);
    }

    [Fact]
    public void InvalidBatchLeavesAllExistingSitesUntouched()
    {
        using var profiles = Store();
        profiles.Save(new Site { Host = "existing.example" });
        var before = File.ReadAllBytes(Path.Combine(root, "sites.json"));
        Assert.Throws<ArgumentException>(() => profiles.ImportSites([new Site { Host = "valid.example" }, new Site { Host = "invalid.example", Protocol = "unknown" }]));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "sites.json")));
        Assert.Single(profiles.List());
    }

    [Fact]
    public void WinScpIniImportStillSkipsS3AndDecodesNames()
    {
        var imported = SiteArchive.Import("\uFEFF[Sessions\\%ED%95%9C%EA%B8%80]\nHostName=ssh.example\nPassword=SECRET-ignored\n[Sessions\\S3]\nHostName=s3.example\nFSProtocol=7");
        Assert.Equal(1, imported.Skipped);
        var site = Assert.Single(imported.Sites);
        Assert.Equal("한글", site.Name);
        Assert.Null(site.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("{\"format\":\"other\",\"version\":1,\"sites\":[]}")]
    [InlineData("{\"format\":\"portway-sites\",\"version\":2,\"sites\":[]}")]
    [InlineData("{\"format\":\"portway-sites\",\"version\":1,\"sites\":[null]}")]
    [InlineData("{\"format\":\"portway-sites\",\"version\":1,\"sites\":[{\"proxy\":null}]}")]
    public void InvalidArchiveIsRejected(string content) => Assert.Throws<ArgumentException>(() => SiteArchive.Import(content));
    [Fact]
    public void ArchiveLimitsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => SiteArchive.Import(new string('가', SiteArchive.MaxBytes / 3 + 1)));
        Assert.Throws<ArgumentException>(() => SiteArchive.Export(Enumerable.Range(0, SiteArchive.MaxSites + 1).Select(_ => new Site { Host = "ssh.example" })));
    }

    [Fact]
    public async Task BrowserExportWorksWhileVaultIsLockedAndEmptyExportFails()
    {
        using var profiles = Store();
        var service = new SiteExportService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Export(profiles, default));
        profiles.Unlock("site-archive-test-master-password");
        profiles.Save(new Site { Host = "ssh.example", Password = "SECRET-stored", SavePassword = true });
        profiles.Lock();
        var result = await service.Export(profiles, default);
        Assert.Equal(1, result.Count);
        Assert.False(result.Cancelled);
        Assert.EndsWith(".json", result.FileName);
        Assert.DoesNotContain("SECRET-stored", result.Content!);
        Assert.Single(SiteArchive.Import(result.Content!).Sites);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
