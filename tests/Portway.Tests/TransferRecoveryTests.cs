using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public class TransferRecoveryTests
{
    static JsonElement Job(TransferQueue queue, string id) => JsonSerializer.SerializeToElement(queue.List(), new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateArray().Single(j => j.GetProperty("id").GetString() == id).Clone();
    static async Task Until(Func<bool> ready)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!ready())
            await Task.Delay(40, limit.Token);
    }

    [IntegrationFact]
    public async Task DurableQueueResumesTheSamePartialFileAfterHostRestartAndVaultUnlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "portway-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = Path.Combine(root, "profile") }).Build();
        var factory = new RemoteFactory();
        var site = (await AdvancedProtocolTests.SshSite()) with
        {
            SavePassword = true
        };
        var remoteRoot = "/home/portway/files/restart-" + Guid.NewGuid().ToString("N");
        var local = Path.Combine(root, "payload.bin");
        var bytes = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        await File.WriteAllBytesAsync(local, bytes);
        await using var inspector = factory.Create(site);
        await inspector.Connect(CancellationToken.None);
        await inspector.CreateDirectory(remoteRoot, CancellationToken.None);
        try
        {
            string id;
            using (var profiles = new ProfileStore(config))
            {
                profiles.Unlock("restart-test-master-password");
                profiles.Save(site);
                await using var connections = new Connections(factory, profiles);
                var connected = JsonSerializer.SerializeToElement(await connections.Connect(site, CancellationToken.None));
                using var queue = new TransferQueue(connections, factory, profiles, new QueueJournal(profiles));
                await queue.StartAsync(CancellationToken.None);
                id = queue.Add(new(connected.GetProperty("id").GetString()!, "upload", [local], remoteRoot, "replace", 64));
                await Until(() => Job(queue, id).GetProperty("bytes").GetInt64() > 0);
                await queue.StopAsync(CancellationToken.None);
                Assert.Equal("paused", Job(queue, id).GetProperty("status").GetString());
                var partial = await inspector.Stat(remoteRoot + "/payload.bin.portway-part-" + id, CancellationToken.None);
                Assert.NotNull(partial);
                Assert.InRange(partial.Size, 1, bytes.Length - 1);
            }

            using (var profiles = new ProfileStore(config))
            {
                await using var connections = new Connections(factory, profiles);
                var journal = new QueueJournal(profiles);
                // 저장된 요청의 테스트용 속도 제한만 해제하고 원본 및 부분 파일 식별자는 그대로 유지합니다.
                var saved = Assert.Single(journal.Load());
                journal.Save(saved with { Request = saved.Request with { SpeedLimit = 0 } });
                using var queue = new TransferQueue(connections, factory, profiles, journal);
                Assert.True(Job(queue, id).GetProperty("restored").GetBoolean());
                Assert.Throws<InvalidOperationException>(() => queue.Control(id, "retry"));
                profiles.Unlock("restart-test-master-password");
                queue.Control(id, "retry");
                await queue.StartAsync(CancellationToken.None);
                await Until(() => Job(queue, id).GetProperty("status").GetString() is "completed" or "failed");
                Assert.Equal("completed", Job(queue, id).GetProperty("status").GetString());
                await queue.StopAsync(CancellationToken.None);
            }

            var download = Path.Combine(root, "download.bin");
            await inspector.Download(remoteRoot + "/payload.bin", download, 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(download));
            Assert.Null(await inspector.Stat(remoteRoot + "/payload.bin.portway-part-" + id, CancellationToken.None));
        }
        finally
        {
            await TransferOperations.DeleteTree(inspector, remoteRoot, CancellationToken.None);
            Directory.Delete(root, true);
        }
    }

    [IntegrationFact]
    public async Task MasksTextModeChecksumsAndMetadataApplyToActualRemoteFiles()
    {
        var site = await AdvancedProtocolTests.SshSite();
        await using var remote = new RemoteFactory().Create(site);
        await remote.Connect(CancellationToken.None);
        var root = Path.Combine(Path.GetTempPath(), "portway-options-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = "/home/portway/files/options-" + Guid.NewGuid().ToString("N");
        await remote.CreateDirectory(target, CancellationToken.None);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "UPPER.txt"), "line one\r\n한글\r\n");
            await File.WriteAllTextAsync(Path.Combine(root, "ignore.bin"), "binary");
            Directory.CreateDirectory(Path.Combine(root, "secret"));
            await File.WriteAllTextAsync(Path.Combine(root, "secret", "hidden.txt"), "excluded");
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            var stamp = DateTimeOffset.UtcNow.AddDays(-2);
            File.SetLastWriteTimeUtc(Path.Combine(root, "UPPER.txt"), stamp.UtcDateTime);
            var options = new TransferOptions
            {
                FileMask = "*.txt|secret/",
                Mode = "text",
                NameCase = "lower",
                ExcludeEmptyDirectories = true,
                VerifyChecksum = true,
                Permissions = "640",
                PreserveTimestamp = true
            };
            await TransferOperations.Transfer(remote, "upload", root, target, "replace", Guid.NewGuid().ToString("N"), (_, _) =>
            {
            }, CancellationToken.None, options: options);
            var output = RemotePaths.Join(target, Path.GetFileName(root));
            var entries = await remote.List(output, CancellationToken.None);
            var file = Assert.Single(entries);
            Assert.Equal("upper.txt", file.Name);
            Assert.Equal("640", file.Permissions);
            Assert.True(Math.Abs((file.Modified - stamp).TotalSeconds) < 2);
            var temp = Path.Combine(root, "download");
            await remote.Download(file.Path, temp, 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal("line one\n한글\n", await File.ReadAllTextAsync(temp));
        }
        finally
        {
            await TransferOperations.DeleteTree(remote, target, CancellationToken.None);
            Directory.Delete(root, true);
        }
    }
}
