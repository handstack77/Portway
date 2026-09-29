using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Portway.Server;

namespace Portway.Tests;

public class ReleaseTests : IDisposable
{
    const string Key = "test-publisher-key-at-least-32-characters";
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-releases-" + Guid.NewGuid().ToString("N"));
    readonly WebApplicationFactory<ServerProgram> factory;
    public ReleaseTests()
    {
        factory = new WebApplicationFactory<ServerProgram>().WithWebHostBuilder(b => b.UseEnvironment("Testing").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Distribution:DataPath"] = root, ["Distribution:ApiKey"] = Key })));
    }

    static byte[] Archive(bool corrupt = false, bool traversal = false)
    {
        var bytes = Encoding.UTF8.GetBytes("package-fixture");
        var asset = new ReleaseStore.Asset("Portway", "0.1.0", "Full", "Portway-0.1.0-full.nupkg", Convert.ToHexString(SHA1.HashData(bytes)), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var stream = zip.CreateEntry(asset.FileName).Open())
                stream.Write(corrupt ? Encoding.UTF8.GetBytes("wrong-package!!") : bytes);
            using (var writer = new StreamWriter(zip.CreateEntry("releases.win-x64-stable.json").Open()))
                writer.Write(JsonSerializer.Serialize(new ReleaseStore.Feed([asset])));
            if (traversal)
            {
                using var stream = zip.CreateEntry("../escape.json").Open();
                stream.WriteByte(1);
            }
        }

        return ms.ToArray();
    }

    async Task<HttpResponseMessage> Publish(HttpClient client, byte[] zip, bool auth = true)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/releases/win-x64-stable")
        {
            Content = new ByteArrayContent(zip)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (auth)
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        return await client.SendAsync(message);
    }

    static (ReleaseStore.Asset Asset, byte[] Bytes) Package(string version, string type = "Full", string? basis = null, string? fileName = null)
    {
        var bytes = Encoding.UTF8.GetBytes($"패키지 {version} {type}");
        return (new ReleaseStore.Asset("Portway", version, type, fileName ?? $"Portway-{version}-{type.ToLowerInvariant()}.nupkg",
            Convert.ToHexString(SHA1.HashData(bytes)), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length)
        { BaseVersion = basis }, bytes);
    }

    static byte[] VersionArchive((ReleaseStore.Asset Asset, byte[] Bytes)[] packages, string? installer = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var package in packages)
            {
                using var stream = zip.CreateEntry(package.Asset.FileName).Open();
                stream.Write(package.Bytes);
            }
            using (var writer = new StreamWriter(zip.CreateEntry("releases.win-x64-stable.json").Open()))
                writer.Write(JsonSerializer.Serialize(new ReleaseStore.Feed(packages.Select(p => p.Asset).ToArray())));
            if (installer != null)
            {
                using var writer = new StreamWriter(zip.CreateEntry("Portway-win-x64-stable-Setup.exe").Open());
                writer.Write(installer);
            }
        }
        return memory.ToArray();
    }

    async Task<ReleaseStore.Feed> Feed(HttpClient client) => JsonSerializer.Deserialize<ReleaseStore.Feed>(
        await client.GetStringAsync("/releases/win-x64-stable/releases.win-x64-stable.json"))!;

    [Fact]
    public async Task VersionOnlyArchivesMergeFullAndDeltaChainsAndCanBeRepublished()
    {
        using var client = factory.CreateClient();
        var first = Package("0.3.9");
        Assert.True((await Publish(client, VersionArchive([first], "첫 설치 파일"))).IsSuccessStatusCode);
        var second = VersionArchive([Package("0.3.10"), Package("0.3.10", "Delta", "0.3.9")], "새 설치 파일");
        Assert.True((await Publish(client, second)).IsSuccessStatusCode);
        Assert.True((await Publish(client, second)).IsSuccessStatusCode);
        Assert.True((await Publish(client, VersionArchive([Package("0.3.11"), Package("0.3.11", "Delta", "0.3.10")]))).IsSuccessStatusCode);
        var feed = await Feed(client);
        Assert.Equal(5, feed.Assets.Length);
        Assert.Equal("0.3.11", feed.Assets[0].Version);
        Assert.Equal(2, feed.Assets.Count(a => a.Type == "Delta"));
        Assert.Equal(first.Bytes, await client.GetByteArrayAsync("/releases/win-x64-stable/" + first.Asset.FileName));
        var legacy = await client.GetStringAsync("/releases/win-x64-stable/RELEASES-win-x64-stable");
        Assert.Equal(5, legacy.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task InvalidDeltaBasisAndChangedVersionLeaveThePublishedFeedUnchanged()
    {
        using var client = factory.CreateClient();
        Assert.True((await Publish(client, VersionArchive([Package("0.3.9")]))).IsSuccessStatusCode);
        var original = await client.GetStringAsync("/releases/win-x64-stable/releases.win-x64-stable.json");
        var invalid = new[]
        {
            VersionArchive([Package("0.3.10"), Package("0.3.10", "Delta", "0.3.8")]),
            VersionArchive([Package("0.3.9", fileName: "different-full.nupkg")]),
            VersionArchive([Package("0.3.10"), Package("0.3.11", "Delta")]),
            VersionArchive([Package("0.3.10"), Package("0.3.10", "Bogus")])
        };
        foreach (var archive in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Publish(client, archive)).StatusCode);
            Assert.Equal(original, await client.GetStringAsync("/releases/win-x64-stable/releases.win-x64-stable.json"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/releases/win-x64-stable/Portway-0.3.10-full.nupkg")).StatusCode);
        }
    }

    [Fact]
    public async Task PublishingAnOlderVersionPreservesLatestInstallerAndSemVerOrder()
    {
        using var client = factory.CreateClient();
        foreach (var version in new[] { "0.4.0-beta.2", "0.4.0-beta.10", "0.4.0", "0.3.9" })
            Assert.True((await Publish(client, VersionArchive([Package(version)], version))).IsSuccessStatusCode);
        Assert.Equal(new[] { "0.4.0", "0.4.0-beta.10", "0.4.0-beta.2", "0.3.9" }, (await Feed(client)).Assets.Select(a => a.Version));
        Assert.Equal("0.4.0", await client.GetStringAsync("/releases/win-x64-stable/Portway-win-x64-stable-Setup.exe"));
    }

    [Fact]
    public async Task StaleDeltaCannotSkipTheCurrentBaseAndReleaseNotesSurviveMerging()
    {
        using var client = factory.CreateClient();
        var first = Package("0.3.9");
        first.Asset = first.Asset with
        {
            Metadata = new Dictionary<string, JsonElement> { ["NotesMarkdown"] = JsonSerializer.SerializeToElement("한글 릴리스 설명") }
        };
        Assert.True((await Publish(client, VersionArchive([first]))).IsSuccessStatusCode);
        Assert.True((await Publish(client, VersionArchive([Package("0.3.10"), Package("0.3.10", "Delta", "0.3.9")]))).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(client, VersionArchive([Package("0.3.11"), Package("0.3.11", "Delta", "0.3.9")]))).StatusCode);
        var feed = await Feed(client);
        Assert.Equal(3, feed.Assets.Length);
        Assert.Equal("한글 릴리스 설명", feed.Assets.Single(a => a.Version == "0.3.9").Metadata!["NotesMarkdown"].GetString());
    }

    [Fact]
    public async Task PublishRequiresAuthAndVerifiesArchive()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Publish(client, Archive(), false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(client, Archive(corrupt: true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Publish(client, Archive(traversal: true))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/releases/win-x64-stable/releases.win-x64-stable.json")).StatusCode);
        var good = await Publish(client, Archive());
        Assert.True(good.IsSuccessStatusCode, await good.Content.ReadAsStringAsync());
        var feed = await client.GetAsync("/releases/win-x64-stable/releases.win-x64-stable.json");
        Assert.Equal(HttpStatusCode.OK, feed.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/releases/win-x64-stable/Portway-0.1.0-full.nupkg");
        request.Headers.Range = new RangeHeaderValue(0, 3);
        var partial = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(4, (await partial.Content.ReadAsByteArrayAsync()).Length);
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("win-x64-stable/..")]
    [InlineData("win")]
    public void UnsafeChannelsRejected(string channel) => Assert.Throws<ArgumentException>(() => ReleaseStore.ValidateChannel(channel));
    public void Dispose()
    {
        factory.Dispose();
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
