using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public class DropTests
{
    sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "portway-drop-test-" + Guid.NewGuid().ToString("N"));
        public ProfileStore Profiles { get; }
        public Connections Connections { get; }
        public TransferQueue Queue { get; }
        public DropStore Drops { get; }

        public Fixture()
        {
            Profiles = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = Root }).Build());
            var factory = new RemoteFactory();
            Connections = new(factory, Profiles);
            Queue = new(Connections, factory, Profiles, new(Profiles));
            Drops = new(Profiles, Queue, NullLogger<DropStore>.Instance);
        }

        public string Create(params DropEntry[] entries) => JsonSerializer.SerializeToElement(Drops.Create(new(entries))).GetProperty("id").GetString()!;
        public string PathFor(string id, string relative) => Path.Combine(Root, "drops", id, "files", relative);
        public async ValueTask DisposeAsync()
        {
            await Queue.StopAsync(CancellationToken.None);
            Drops.Dispose();
            Queue.Dispose();
            await Connections.DisposeAsync();
            Profiles.Dispose();
            Directory.Delete(Root, true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("file:stream")]
    [InlineData("folder\\escape")]
    [InlineData("NUL.txt")]
    [InlineData("CONIN$")]
    [InlineData("trail.")]
    [InlineData("trail ")]
    public void RejectsUnsafeOrNonPortablePaths(string path) => Assert.Throws<ArgumentException>(() => DropStore.Validate([new(path, false)]));
    [Fact]
    public void RejectsCollisionsMissingParentsAndImpossibleSizes()
    {
        Assert.Throws<ArgumentException>(() => DropStore.Validate([new("A.txt", false), new("a.txt", false)]));
        Assert.Throws<ArgumentException>(() => DropStore.Validate([new("caf\u00e9.txt", false), new("cafe\u0301.txt", false)]));
        Assert.Throws<ArgumentException>(() => DropStore.Validate([new("a/b.txt", false)]));
        Assert.Throws<ArgumentException>(() => DropStore.Validate([new("a", false), new("a/b", false)]));
        Assert.Throws<ArgumentException>(() => DropStore.Validate([new("a", false, -1)]));
        Assert.Throws<ArgumentException>(() => DropStore.Validate([new("a", true, 1)]));
    }

    [Fact]
    public async Task StreamsChunksAndRollsBackInterruptedOrOversizedBodies()
    {
        await using var f = new Fixture();
        var id = f.Create(new DropEntry("한글.txt", false, 8));
        Assert.Equal(3, await f.Drops.Append(id, 0, 0, 3, new MemoryStream("abc"u8.ToArray()), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Drops.Append(id, 0, 0, 3, new MemoryStream("abc"u8.ToArray()), default));
        await Assert.ThrowsAsync<EndOfStreamException>(() => f.Drops.Append(id, 0, 3, 5, new MemoryStream("de"u8.ToArray()), default));
        Assert.Equal("abc", await File.ReadAllTextAsync(f.PathFor(id, "한글.txt")));
        await Assert.ThrowsAsync<IOException>(() => f.Drops.Append(id, 0, 3, 3, new MemoryStream("defg"u8.ToArray()), default));
        Assert.Equal("abc", await File.ReadAllTextAsync(f.PathFor(id, "한글.txt")));
        Assert.Equal(8, await f.Drops.Append(id, 0, 3, 5, new MemoryStream("defgh"u8.ToArray()), default));
        Assert.Equal("abcdefgh", await File.ReadAllTextAsync(f.PathFor(id, "한글.txt")));
        await f.Drops.Abandon(id, default);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "drops", id)));
    }

    [Fact]
    public async Task PreservesEmptyFoldersAndRejectsIncompleteCommit()
    {
        await using var f = new Fixture();
        var id = f.Create(new("root", true), new("root/empty", true), new("root/zero", false), new("root/full", false, 2));
        Assert.True(Directory.Exists(f.PathFor(id, "root/empty")));
        Assert.Equal(0, new FileInfo(f.PathFor(id, "root/zero")).Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Drops.Commit(id, new("unused", "upload", [], "/"), default));
        await f.Drops.Sweep(default);
        Assert.True(Directory.Exists(f.PathFor(id, "root")));
    }

    [IntegrationFact]
    public async Task CleanupKeepsSnapshotReferencedByAnotherJobAndIgnoresInvalidUnrelatedPaths()
    {
        await using var f = new Fixture();
        var site = await AdvancedProtocolTests.SshSite();
        var session = JsonSerializer.SerializeToElement(await f.Connections.Connect(site, default)).GetProperty("id").GetString()!;
        var id = f.Create(new DropEntry("zero", false));
        var first = await f.Drops.Commit(id, new(session, "upload", [], "/unused"), default);
        var journal = new QueueJournal(f.Profiles);
        var saved = Assert.Single(journal.Load());
        journal.Save(saved with { Status = "completed" });
        journal.Save(saved with { Id = Guid.NewGuid().ToString("N"), Status = "paused" });
        // 다른 곳의 잘못된 실패 작업 때문에 백그라운드 정리 서비스가 중단되면 안 됩니다.
        journal.Save(saved with { Id = Guid.NewGuid().ToString("N"), Status = "failed", Request = saved.Request with { Paths = ["bad\0path"] } });
        using var restored = new TransferQueue(f.Connections, new RemoteFactory(), f.Profiles, journal);
        using var drops = new DropStore(f.Profiles, restored, NullLogger<DropStore>.Instance);
        Assert.Equal("paused", restored.FindSourceJob(Path.Combine(f.Root, "drops", id, "files"))?.Status);
        await drops.Sweep(default);
        Assert.True(File.Exists(f.PathFor(id, "zero")));
    }

    [IntegrationFact]
    public async Task RealSftpDropIsIdempotentSurvivesRecreationAndCleansOnlyCompletedSnapshots()
    {
        await using var f = new Fixture();
        var site = await AdvancedProtocolTests.SshSite();
        var session = JsonSerializer.SerializeToElement(await f.Connections.Connect(site, default)).GetProperty("id").GetString()!;
        await using var remote = new RemoteFactory().Create(site);
        await remote.Connect(default);
        var target = "/home/portway/files/drop-" + Guid.NewGuid().ToString("N");
        await remote.CreateDirectory(target, default);
        var bytes = Encoding.UTF8.GetBytes("external drop 한글\n");
        var stamp = DateTimeOffset.UtcNow.AddDays(-1);
        var id = f.Create(new("project", true), new("project/empty", true), new("project/한글.txt", false, bytes.Length, stamp), new("zero.txt", false));
        await f.Drops.Append(id, 2, 0, bytes.Length, new MemoryStream(bytes), default);
        var request = new TransferRequest(session, "upload", [], target, "replace", Options: new() { VerifyChecksum = true, PreserveTimestamp = true });
        var job = await f.Drops.Commit(id, request, default);
        Assert.Equal(job, await f.Drops.Commit(id, request, default));
        Assert.Single(f.Queue.List());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Drops.Abandon(id, default));
        using (var recreated = new DropStore(f.Profiles, f.Queue, NullLogger<DropStore>.Instance))
        {
            await recreated.Sweep(default);
            Assert.True(File.Exists(f.PathFor(id, "project/한글.txt")));
        }

        try
        {
            await f.Queue.StartAsync(default);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (f.Queue.FindSourceJob(Path.Combine(f.Root, "drops", id, "files"))?.Status is "queued" or "running")
                await Task.Delay(30, limit.Token);
            Assert.Equal("completed", f.Queue.FindSourceJob(Path.Combine(f.Root, "drops", id, "files"))?.Status);
            Assert.True((await remote.Stat(target + "/project/empty", default))!.IsDirectory);
            Assert.Equal(0, (await remote.Stat(target + "/zero.txt", default))!.Size);
            var output = Path.Combine(f.Root, "readback");
            await remote.Download(target + "/project/한글.txt", output, 0, _ =>
            {
            }, default);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(output));
            Assert.InRange(Math.Abs(((await remote.Stat(target + "/project/한글.txt", default))!.Modified - stamp).TotalSeconds), 0, 2);
            await f.Drops.Sweep(default);
            Assert.False(Directory.Exists(Path.Combine(f.Root, "drops", id)));
        }
        finally
        {
            await TransferOperations.DeleteTree(remote, target, default);
        }
    }
}
