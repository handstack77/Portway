using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public sealed class LocalTransferTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-local-" + Guid.NewGuid().ToString("N"));
    string Folder(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
    string Source(string name, string content = "한글\r\n파일\n")
    {
        var path = Path.Combine(Folder("left"), name);
        File.WriteAllText(path, content);
        return path;
    }

    TransferRequest Request(string[] paths, string destination, string conflict = "replace", TransferOptions? options = null) => new("", "local", paths, destination, conflict, Options: options);
    async Task Copy(TransferRequest request)
    {
        request = LocalTransferFileSystem.Validate(request);
        await using var fs = new LocalTransferFileSystem();
        foreach (var path in request.Paths)
            await TransferOperations.Transfer(fs, "download", path, request.Destination, request.Conflict, Guid.NewGuid().ToString("N"), (_, _) =>
            {
            }, CancellationToken.None, options: request.Options);
    }

    [Fact]
    public async Task CopiesNestedEmptyFoldersAndBinaryContentWithoutNewlineConversion()
    {
        var folder = Folder("left/자료/빈 폴더");
        var source = Path.Combine(Folder("left/자료/하위"), "한글.bin");
        var content = new byte[]
        {
            0,
            255,
            13,
            10,
            42
        };
        await File.WriteAllBytesAsync(source, content);
        var right = Folder("right");
        await Copy(Request([Path.Combine(root, "left", "자료")], right, options: new() { Mode = "text", VerifyChecksum = true }));
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(right, "자료", "하위", "한글.bin")));
        Assert.True(Directory.Exists(Path.Combine(right, "자료", "빈 폴더")));
        Assert.True(File.Exists(source));
    }

    [Theory]
    [InlineData("skip", "기존")]
    [InlineData("replace", "원본")]
    [InlineData("rename", "기존")]
    [InlineData("newer", "기존")]
    public async Task ResolvesConflictsWithoutDeletingSkippedMoveSources(string conflict, string expected)
    {
        var source = Source("same.txt", "원본");
        var target = Path.Combine(Folder("right"), "same.txt");
        File.WriteAllText(target, "기존");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        await Copy(Request([source], Path.GetDirectoryName(target)!, conflict, new() { RemoveSource = true }));
        Assert.Equal(expected, File.ReadAllText(target));
        if (conflict is "skip" or "newer")
            Assert.True(File.Exists(source));
        else
            Assert.False(File.Exists(source));
        if (conflict == "rename")
            Assert.Equal("원본", File.ReadAllText(target + " (1)"));
    }

    [Fact]
    public async Task FilteredMovePreservesExcludedFilesAndMovesOnlyVerifiedFiles()
    {
        var included = Source("keep.txt");
        var excluded = Source("leave.bin");
        var right = Folder("right");
        await Copy(Request([Path.GetDirectoryName(included)!], right, options: new() { FileMask = "*.txt", VerifyChecksum = true, RemoveSource = true }));
        Assert.False(File.Exists(included));
        Assert.True(File.Exists(excluded));
        Assert.True(File.Exists(Path.Combine(right, "left", "keep.txt")));
        Assert.False(File.Exists(Path.Combine(right, "left", "leave.bin")));
    }

    [Fact]
    public void RejectsSameDestinationDescendantsAndDuplicateTargetNamesBeforeWriting()
    {
        var source = Source("same.txt");
        var left = Path.GetDirectoryName(source)!;
        var child = Folder("left/child");
        var right = Folder("right");
        Assert.Throws<IOException>(() => LocalTransferFileSystem.Validate(Request([source], left)));
        Assert.Throws<IOException>(() => LocalTransferFileSystem.Validate(Request([left], child)));
        Assert.Throws<IOException>(() => LocalTransferFileSystem.Validate(Request([source, source], right)));
        Assert.Throws<IOException>(() => LocalTransferFileSystem.Validate(Request([left, source], right)));
        Assert.Equal("한글\r\n파일\n", File.ReadAllText(source));
        Assert.Empty(Directory.EnumerateFileSystemEntries(right));
    }

    [LocalLinkFact]
    public async Task RefusesLinkedSourcesAndDestinationAncestors()
    {
        var source = Source("source.txt");
        var right = Folder("right");
        var link = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(link, right);
        Assert.Throws<IOException>(() => LocalTransferFileSystem.Validate(Request([source], link)));
        var fileLink = Path.Combine(root, "left", "linked.txt");
        File.CreateSymbolicLink(fileLink, source);
        Assert.Throws<IOException>(() => LocalTransferFileSystem.Validate(Request([fileLink], right)));
        await Assert.ThrowsAsync<IOException>(() => Copy(Request([Path.GetDirectoryName(source)!], right)));
        Assert.Equal("한글\r\n파일\n", File.ReadAllText(source));
    }

    [Fact]
    public async Task QueueRestoresAndResumesLocalCopyWithoutAnyServerConnection()
    {
        var source = Source("large.bin", new string('x', 1024 * 1024));
        var right = Folder("right");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = Folder("profile") }).Build();
        string id;
        using (var profiles = new ProfileStore(config))
        {
            var factory = new RemoteFactory();
            await using var connections = new Connections(factory, profiles);
            using var queue = new TransferQueue(connections, factory, profiles, new QueueJournal(profiles));
            await queue.StartAsync(CancellationToken.None);
            id = queue.Add(Request([source], right) with { SpeedLimit = 32 });
            await Until(() => Job(queue, id).GetProperty("bytes").GetInt64() > 0);
            await queue.StopAsync(CancellationToken.None);
            Assert.Equal("paused", Job(queue, id).GetProperty("status").GetString());
            Assert.False(File.Exists(Path.Combine(right, "large.bin")));
        }

        using (var profiles = new ProfileStore(config))
        {
            var factory = new RemoteFactory();
            await using var connections = new Connections(factory, profiles);
            var journal = new QueueJournal(profiles);
            var checkpoint = Assert.Single(journal.Load());
            journal.Save(checkpoint with { Request = checkpoint.Request with { SpeedLimit = 0 } });
            using var queue = new TransferQueue(connections, factory, profiles, journal);
            queue.Control(id, "retry");
            await queue.StartAsync(CancellationToken.None);
            await Until(() => Job(queue, id).GetProperty("status").GetString() is "completed" or "failed");
            Assert.Equal("completed", Job(queue, id).GetProperty("status").GetString());
            await queue.StopAsync(CancellationToken.None);
        }

        Assert.Equal(await FileChecksums.Local(source, "sha256", CancellationToken.None), await FileChecksums.Local(Path.Combine(right, "large.bin"), "sha256", CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(right, "*.portway-part-*"));
    }

    static JsonElement Job(TransferQueue queue, string id) => JsonSerializer.SerializeToElement(queue.List(), new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateArray().Single(job => job.GetProperty("id").GetString() == id).Clone();
    static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!ready())
            await Task.Delay(30, timeout.Token);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

public sealed class LocalLinkFactAttribute : FactAttribute
{
    public LocalLinkFactAttribute()
    {
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("PORTWAY_SYMLINK_TESTS") != "1")
            Skip = "Windows 심볼릭 링크 생성 권한이 필요합니다. 권한이 있는 환경에서 PORTWAY_SYMLINK_TESTS=1로 실행하세요.";
    }
}
