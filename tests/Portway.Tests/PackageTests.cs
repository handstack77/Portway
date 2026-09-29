using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portway.Server;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace Portway.Tests;

public sealed class PackageFactAttribute : FactAttribute
{
    public PackageFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_TEST") == null)
            Skip = "Set PORTWAY_PACKAGE_TEST to a generated publisher ZIP.";
    }
}

public sealed class PackageDeltaTheoryAttribute : TheoryAttribute
{
    public PackageDeltaTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_TEST") == null ||
            Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_BASE") == null || Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_UPDATER") == null)
            Skip = "실제 Windows 변경분 검사에는 ZIP, 기준 Full 패키지와 격리된 Update.exe 경로가 필요합니다.";
    }
}

public class PackageTests
{
    [PackageDeltaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealDeltaDownloadsAndReconstructsFullWithCorruptionFallback(bool corruptDelta)
    {
        var archive = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_TEST")!;
        var basis = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_BASE")!;
        var updater = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_UPDATER")!;
        var channel = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_CHANNEL") ?? "win-x64-stable";
        var data = Path.Combine(Path.GetTempPath(), "portway-delta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            var basisFeed = JsonSerializer.Deserialize<ReleaseStore.Feed>(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(basis)!, $"releases.{channel}.json")))!;
            var basisAsset = basisFeed.Assets.Single(a => a.FileName == Path.GetFileName(basis));
            var store = new ReleaseStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Distribution:DataPath"] = Path.Combine(data, "server") }).Build());
            var first = Path.Combine(data, "base.zip");
            using (var zip = ZipFile.Open(first, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(basis, basisAsset.FileName);
                using var writer = new StreamWriter(zip.CreateEntry($"releases.{channel}.json").Open());
                writer.Write(JsonSerializer.Serialize(new ReleaseStore.Feed([basisAsset])));
            }
            await using (var stream = File.OpenRead(first))
                await store.Publish(channel, stream, CancellationToken.None);
            await using (var stream = File.OpenRead(archive))
                await store.Publish(channel, stream, CancellationToken.None);
            var packages = Path.Combine(data, "packages");
            Directory.CreateDirectory(packages);
            File.Copy(basis, Path.Combine(packages, Path.GetFileName(basis)));
            var local = new VelopackAsset
            {
                PackageId = basisAsset.PackageId,
                Version = SemanticVersion.Parse(basisAsset.Version),
                Type = VelopackAssetType.Full,
                FileName = basisAsset.FileName,
                SHA1 = basisAsset.SHA1,
                SHA256 = basisAsset.SHA256,
                Size = basisAsset.Size
            };
            var locator = new TestVelopackLocator("Portway", basisAsset.Version, packages, Path.Combine(data, "current"), data, updater, channel, localPackage: local);
            var source = new RecordingSource(new DirectoryInfo(Path.Combine(data, "server", channel)), corruptDelta);
            var manager = new UpdateManager(source, locator: locator);
            var update = await manager.CheckForUpdatesAsync();
            Assert.NotNull(update);
            Assert.NotEmpty(update.DeltasToTarget);
            Assert.All(update.DeltasToTarget, a => Assert.Equal(VelopackAssetType.Delta, a.Type));
            await manager.DownloadUpdatesAsync(update);
            var result = Path.Combine(packages, update.TargetFullRelease.FileName);
            // 변경분 복원은 ZIP을 다시 압축하므로 컨테이너 해시 대신 모든 파일의 내용을 비교합니다.
            using (var reconstructed = ZipFile.OpenRead(result))
            using (var original = ZipFile.OpenRead(Path.Combine(data, "server", channel, update.TargetFullRelease.FileName)))
            {
                Assert.Equal(original.Entries.Select(e => e.FullName).Order(), reconstructed.Entries.Select(e => e.FullName).Order());
                foreach (var entry in original.Entries)
                {
                    await using var expected = entry.Open();
                    await using var actual = reconstructed.GetEntry(entry.FullName)!.Open();
                    Assert.Equal(await SHA256.HashDataAsync(expected), await SHA256.HashDataAsync(actual));
                }
            }
            Assert.Contains(VelopackAssetType.Delta, source.Downloads);
            if (corruptDelta)
            {
                Assert.Contains(VelopackAssetType.Full, source.Downloads);
                await using var package = File.OpenRead(result);
                Assert.Equal(update.TargetFullRelease.SHA256, Convert.ToHexString(await SHA256.HashDataAsync(package)));
            }
            else
                Assert.DoesNotContain(VelopackAssetType.Full, source.Downloads);
        }
        finally
        {
            Directory.Delete(data, true);
        }
    }

    sealed class RecordingSource(DirectoryInfo directory, bool corruptDelta) : SimpleFileSource(directory)
    {
        public List<VelopackAssetType> Downloads { get; } = [];
        public override async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
        {
            Downloads.Add(releaseEntry.Type);
            await base.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
            if (corruptDelta && releaseEntry.Type == VelopackAssetType.Delta)
                await File.WriteAllTextAsync(localFile, "손상된 변경분 검사", cancelToken);
        }
    }

    [PackageFact]
    public async Task GeneratedVpkArchivePublishesAndDownloadsWithMatchingHash()
    {
        var archive = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_TEST");
        var channel = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_CHANNEL") ?? "win-x64-stable";
        if (archive == null)
            return; // 명시적으로 활성화한 경우에만 실행하며 일반 단위 테스트에서는 바이너리를 빌드하지 않습니다.
        var data = Path.Combine(Path.GetTempPath(), "portway-package-" + Guid.NewGuid().ToString("N"));
        const string key = "isolated-in-process-publisher-test-key";
        try
        {
            await using var factory = new WebApplicationFactory<ServerProgram>().WithWebHostBuilder(b => b.UseEnvironment("Testing").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Distribution:DataPath"] = data, ["Distribution:ApiKey"] = key })));
            using var client = factory.CreateClient();
            var basis = Environment.GetEnvironmentVariable("PORTWAY_PACKAGE_BASE");
            if (basis != null)
            {
                Directory.CreateDirectory(data);
                var basisFeed = JsonSerializer.Deserialize<ReleaseStore.Feed>(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(basis)!, $"releases.{channel}.json")))!;
                var basisAsset = basisFeed.Assets.Single(a => a.FileName == Path.GetFileName(basis));
                var basisArchive = Path.Combine(data, "basis.zip");
                using (var basisZip = ZipFile.Open(basisArchive, ZipArchiveMode.Create))
                {
                    basisZip.CreateEntryFromFile(basis, basisAsset.FileName);
                    using var writer = new StreamWriter(basisZip.CreateEntry($"releases.{channel}.json").Open());
                    writer.Write(JsonSerializer.Serialize(new ReleaseStore.Feed([basisAsset])));
                }
                await using var basisStream = File.OpenRead(basisArchive);
                await factory.Services.GetRequiredService<ReleaseStore>().Publish(channel, basisStream, CancellationToken.None);
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/releases/" + channel)
            {
                Content = new StreamContent(File.OpenRead(archive))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            var response = await client.SendAsync(request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var feed = JsonSerializer.Deserialize<ReleaseStore.Feed>(await client.GetStringAsync($"/releases/{channel}/releases.{channel}.json"))!;
            var asset = feed.Assets.Where(a => a.Type == "Full").OrderByDescending(a => Version.Parse(a.Version.Split('-')[0])).First();
            await using var stream = await client.GetStreamAsync($"/releases/{channel}/" + asset.FileName);
            Assert.Equal(asset.SHA256, Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream)));
            using var zip = ZipFile.OpenRead(archive);
            if (basis != null)
            {
                using var feedStream = zip.GetEntry($"releases.{channel}.json")!.Open();
                var uploaded = (await JsonSerializer.DeserializeAsync<ReleaseStore.Feed>(feedStream))!;
                Assert.Single(uploaded.Assets.Select(a => a.Version).Distinct());
                Assert.Equal(uploaded.Assets.Length, zip.Entries.Count(e => e.FullName.EndsWith(".nupkg")));
                Assert.DoesNotContain(zip.Entries, e => e.FullName == Path.GetFileName(basis));
            }
            var installerExtension = channel.StartsWith("win-") ? "Setup.exe" : channel.StartsWith("linux-") ? ".AppImage" : ".pkg";
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith(installerExtension));
        }
        finally
        {
            if (Directory.Exists(data))
                Directory.Delete(data, true);
        }
    }
}
