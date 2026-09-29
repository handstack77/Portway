using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Portway.Core;
using Portway.Core.Protocols;
using Portway.Desktop;

namespace Portway.Tests;

public class AdvancedTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-advanced-" + Guid.NewGuid().ToString("N"));
    public AdvancedTests() => Directory.CreateDirectory(root);
    IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = root }).Build();

    [Fact]
    public void NestedCredentialsAreEncryptedAndBoundToTheirEndpoint()
    {
        using var store = new ProfileStore(Configuration);
        store.Unlock("master-password-for-tests");
        var site = new Site
        {
            Host = "server",
            Username = "user",
            SavePassword = true,
            Password = "secret-password",
            SessionToken = "secret-session",
            ClientCertificatePassword = "secret-cert",
            Proxy = new("http", "proxy", 80, "puser", "secret-proxy"),
            Jump = new Site
            {
                Host = "jump",
                Username = "jump-user",
                Password = "secret-jump",
                Passphrase = "secret-phrase"
            }
        };
        store.Save(site);
        Assert.DoesNotContain("secret-", File.ReadAllText(Path.Combine(root, "sites.json")));
        var publicSite = Assert.Single(store.List());
        Assert.False(SiteSecrets.HasAny(publicSite));
        var hydrated = store.Hydrate(publicSite);
        Assert.Equal(site, hydrated with { HasPassword = false });
        Assert.Null(store.Hydrate(publicSite with { Proxy = publicSite.Proxy with { Host = "other" } }).Proxy.Password);
        Assert.Null(store.Hydrate(publicSite with { Jump = publicSite.Jump! with { Host = "other" } }).Jump!.Password);
    }

    [Theory]
    [InlineData("*.txt;*.csv | *.bak", "report.csv", false, 10, true)]
    [InlineData("*.txt | secret/", "secret", true, 0, false)]
    [InlineData("*.txt", "subfolder", true, 0, true)]
    [InlineData("*.txt>1K<2K", "a.txt", false, 1500, true)]
    [InlineData("*.txt>1K<2K", "a.txt", false, 3000, false)]
    [InlineData("*.*", "README", false, 1, true)]
    [InlineData("*.", "README.md", false, 1, false)]
    public void MasksRespectTypesConstraintsAndWinScpStarDotStar(string mask, string name, bool directory, long size, bool expected) => Assert.Equal(expected, new FileMask(mask).Matches(new(name, "/" + name, directory, size, DateTimeOffset.UtcNow), name));
    [Fact]
    public async Task TextConversionHandlesCrLfAcrossBufferBoundaryWithoutLosingBytes()
    {
        var path = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");
        var text = new string('a', 131071) + "\r\n한글\rlast\n";
        await File.WriteAllTextAsync(path, text);
        await TextTransfer.ConvertNewlines(path, output, false, CancellationToken.None);
        Assert.Equal(new string('a', 131071) + "\n한글\nlast\n", await File.ReadAllTextAsync(output));
        await File.WriteAllBytesAsync(path, [0, 1, 2]);
        await Assert.ThrowsAsync<IOException>(() => TextTransfer.ConvertNewlines(path, output, false, CancellationToken.None));
    }

    [Fact]
    public void JournalRestoresCheckpointWithoutPersistingAnySecret()
    {
        using var profiles = new ProfileStore(Configuration);
        var journal = new QueueJournal(profiles);
        var id = Guid.NewGuid().ToString("N");
        var request = new TransferRequest("old-session", "upload", ["source"], "/target", Options: new() { VerifyChecksum = true });
        var site = new Site
        {
            Host = "host",
            Password = "dont-store-password",
            Proxy = new("http", "proxy", 80, "user", "dont-store-proxy")
        };
        var checkpoint = new TransferCheckpoint(id, request, site, "running", null, DateTimeOffset.UtcNow, [], [], [], [], Bytes: 1024, Total: 2048);
        journal.Save(checkpoint);
        var reopened = new QueueJournal(profiles);
        var loaded = Assert.Single(reopened.Load());
        Assert.Equal(1024, loaded.Bytes);
        Assert.False(SiteSecrets.HasAny(loaded.Site));
        Assert.DoesNotContain("dont-store", File.ReadAllText(Path.Combine(root, "queue", id + ".json")));
        using var connections = new AsyncDisposer(new Connections(new RemoteFactory(), profiles));
        using var queue = new TransferQueue(connections.Value, new RemoteFactory(), profiles, reopened);
        var state = JsonSerializer.SerializeToElement(Assert.Single(queue.List()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("paused", state.GetProperty("status").GetString());
        Assert.True(state.GetProperty("restored").GetBoolean());
    }

    sealed class AsyncDisposer(Connections value) : IDisposable
    {
        public Connections Value => value;

        public void Dispose() => value.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose() => Directory.Delete(root, true);
}
