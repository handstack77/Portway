using Microsoft.Extensions.Configuration;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public class CoreTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-tests-" + Guid.NewGuid().ToString("N"));
    public CoreTests() => Directory.CreateDirectory(root);
    [Theory]
    [InlineData("/a/../b//c", "/b/c")]
    [InlineData("../../", "/")]
    [InlineData("/한글 file/", "/한글 file")]
    public void RemotePathsNormalize(string input, string expected) => Assert.Equal(expected, RemotePaths.Normalize(input));
    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void RemoteNamesCannotEscapeDownloadFolder(string name) => Assert.Throws<IOException>(() => RemotePaths.SafeLocalChild(root, name));
    [Fact]
    public void VaultRequiresMasterPasswordAndNeverStoresPlaintext()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = root }).Build();
        var site = new Site
        {
            Name = "test",
            Host = "localhost",
            Username = "user",
            Password = "SENSITIVE-secret",
            SavePassword = true
        };
        using (var store = new ProfileStore(config))
        {
            Assert.Throws<InvalidOperationException>(() => store.Save(site));
            store.Unlock("a-long-test-master-password");
            store.Save(site);
            Assert.DoesNotContain("SENSITIVE-secret", File.ReadAllText(Path.Combine(root, "sites.json")));
            Assert.Null(store.List().Single().Password);
            store.Lock();
            Assert.Throws<InvalidOperationException>(() => store.Hydrate(site with { Password = null }));
            Assert.Throws<ArgumentException>(() => store.Unlock("incorrect-password"));
        }

        using var reopened = new ProfileStore(config);
        reopened.Unlock("a-long-test-master-password");
        Assert.Equal(site.Password, reopened.Hydrate(site with { Password = null }).Password);
        Assert.Null(reopened.Hydrate(site with { Host = "different-host", Password = null }).Password);
    }

    [Fact]
    public void EditorRejectsConcurrentModification()
    {
        var path = Path.Combine(root, "document.txt");
        File.WriteAllText(path, "original");
        var etag = LocalFiles.Hash(File.ReadAllBytes(path));
        File.WriteAllText(path, "someone else's edit");
        Assert.Throws<InvalidOperationException>(() => LocalFiles.Save(new(path, Content: "overwrite", Etag: etag)));
        Assert.Equal("someone else's edit", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("http://example.com/updates")]
    [InlineData("file:///tmp/updates")]
    [InlineData("https://user:password@example.com")]
    public void UpdateUrlsRequireHttps(string value) => Assert.Throws<ArgumentException>(() => UpdateService.ValidateUrl(value));
    [Fact]
    public void WinScpImportMapsProtocolsAndDropsSecrets()
    {
        var sites = SiteImport.FromWinScpIni("[Sessions\\Test%20SSH]\nHostName=example.com\nFSProtocol=2\nPassword=should-not-import\n[Sessions\\Legacy]\nHostName=scp.example.com\nFSProtocol=0\n[Sessions\\SecureFTP]\nHostName=ftp.example.com\nFSProtocol=5\nFtpSecure=3");
        Assert.Equal(3, sites.Length);
        Assert.Equal("Test SSH", sites[0].Name);
        Assert.Equal("sftp", sites[0].Protocol);
        Assert.Equal("scp", sites[1].Protocol);
        Assert.Equal("ftps", sites[2].Protocol);
        Assert.All(sites, s => Assert.Null(s.Password));
    }

    public void Dispose() => Directory.Delete(root, true);
}
