using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public sealed class WorkflowTests
{
    sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    sealed class Interaction : IAuthenticationInteraction
    {
        public Task<string[]> Answer(Site s, string i, AuthenticationPrompt[] p, CancellationToken ct) => throw new NotSupportedException();
    }

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    static async Task Until(Func<Task<bool>> ready)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (!await ready())
            await Task.Delay(100, limit.Token);
    }

    sealed class Fixture : IAsyncDisposable
    {
        public string Root = Path.Combine(Path.GetTempPath(), "portway-workflow-" + Guid.NewGuid().ToString("N"));
        public string Remote = "/home/portway/files/workflow-" + Guid.NewGuid().ToString("N");
        public ProfileStore Profiles = null!;
        public Connections Connections = null!;
        public IRemoteFileSystem Files = null!;
        public RemoteFactory Factory = new();
        public string Session = "";
        public static async Task<Fixture> Open()
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.Root);
            var site = await AdvancedProtocolTests.SshSite();
            f.Profiles = new ProfileStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = Path.Combine(f.Root, "profile") }).Build());
            f.Connections = new(f.Factory, f.Profiles);
            f.Session = Json(await f.Connections.Connect(site, CancellationToken.None)).GetProperty("id").GetString()!;
            f.Files = f.Factory.Create(site);
            await f.Files.Connect(CancellationToken.None);
            await f.Files.CreateDirectory(f.Remote, CancellationToken.None);
            return f;
        }

        public async Task Upload(string name, string content)
        {
            var path = Path.Combine(Root, "payload");
            await File.WriteAllTextAsync(path, content);
            await Files.Upload(path, Remote + "/" + name, 0, _ =>
            {
            }, CancellationToken.None);
        }

        public async Task<string> Read(string name)
        {
            var path = Path.Combine(Root, "read");
            await Files.Download(Remote + "/" + name, path, 0, _ =>
            {
            }, CancellationToken.None);
            return await File.ReadAllTextAsync(path);
        }

        public async ValueTask DisposeAsync()
        {
            await TransferOperations.DeleteTree(Files, Remote, CancellationToken.None);
            await Files.DisposeAsync();
            await Connections.DisposeAsync();
            Profiles.Dispose();
            Directory.Delete(Root, true);
        }
    }

    [Theory]
    [InlineData("utf-8", true)]
    [InlineData("utf-16le", true)]
    [InlineData("utf-16be", true)]
    [InlineData("utf-32be", true)]
    [InlineData("cp949", false)]
    public void EditorPreservesEncodingBomAndRefusesUnrepresentableCharacters(string encoding, bool bom)
    {
        var bytes = TextCodec.Encode("한글\r\nedit", encoding, bom);
        var decoded = TextCodec.Decode(bytes, bom ? null : encoding);
        Assert.Equal("한글\r\nedit", decoded.Content);
        Assert.Equal(bom, decoded.Bom);
        Assert.Equal(bytes, TextCodec.Encode(decoded.Content, decoded.Encoding, decoded.Bom));
        Assert.Throws<EncoderFallbackException>(() => TextCodec.Encode("😀", "cp949", false));
    }

    [IntegrationFact]
    public async Task SynchronizationRejectsMetadataPreservingEditsAndAppliesSelectedConflictResolution()
    {
        await using var f = await Fixture.Open();
        var local = Path.Combine(f.Root, "sync");
        Directory.CreateDirectory(local);
        var a = Path.Combine(local, "a.txt");
        File.WriteAllText(a, "local");
        File.WriteAllText(Path.Combine(local, "b.txt"), "keep");
        await f.Upload("a.txt", "other");
        var stamp = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(a, stamp);
        await f.Files.SetModified(f.Remote + "/a.txt", stamp, CancellationToken.None);
        var req = new SyncRequest(f.Session, local, f.Remote, "both");
        var plan = await Synchronizer.Preview(f.Files, req, CancellationToken.None);
        Assert.Contains(plan.Changes, c => c.Action == "conflict");
        File.WriteAllText(a, "newer");
        File.SetLastWriteTimeUtc(a, stamp);
        await Assert.ThrowsAsync<IOException>(() => Synchronizer.Apply(f.Files, req, plan, _ =>
        {
        }, CancellationToken.None));
        plan = await Synchronizer.Preview(f.Files, req, CancellationToken.None);
        await Synchronizer.Apply(f.Files, req, plan, _ =>
        {
        }, CancellationToken.None, ["a.txt"], new Dictionary<string, string> { { "a.txt", "download" } });
        Assert.Equal("other", File.ReadAllText(a));
        Assert.Null(await f.Files.Stat(f.Remote + "/b.txt", CancellationToken.None));
    }

    [IntegrationFact]
    public async Task LiveSyncObservesAtomicSavesPausesAndRestoresPaused()
    {
        await using var f = await Fixture.Open();
        var local = Path.Combine(f.Root, "watch");
        Directory.CreateDirectory(local);
        using (var watches = new LiveSyncService(f.Connections, f.Factory, f.Profiles))
        {
            await watches.StartAsync(CancellationToken.None);
            var id = watches.Add(new(new(f.Session, local, f.Remote), 2));
            File.WriteAllText(Path.Combine(local, "a.txt"), "initial");
            await Until(async () => await f.Files.Stat(f.Remote + "/a.txt", CancellationToken.None) != null);
            var temp = Path.Combine(local, "save.tmp");
            File.WriteAllText(temp, "atomic-save");
            File.Move(temp, Path.Combine(local, "a.txt"), true);
            await Until(async () => await f.Read("a.txt") == "atomic-save");
            await watches.Control(id, "pause", null);
            File.WriteAllText(Path.Combine(local, "a.txt"), "paused-edit");
            await Task.Delay(2600);
            Assert.Equal("atomic-save", await f.Read("a.txt"));
            await watches.StopAsync(CancellationToken.None);
        }

        using var restored = new LiveSyncService(f.Connections, f.Factory, f.Profiles);
        Assert.Equal("paused", Json(restored.List())[0].GetProperty("status").GetString());
        Assert.DoesNotContain("portway-test-only", File.ReadAllText(Path.Combine(f.Profiles.DataPath, "watches.json")));
    }

    [IntegrationFact]
    public async Task ExternalEditorUploadsSavesDetectsConflictAndKeepsRecoveryCopy()
    {
        await using var f = await Fixture.Open();
        await f.Upload("edit.txt", "original");
        await f.Files.Chmod(f.Remote + "/edit.txt", "640", CancellationToken.None);
        await using var editor = new ExternalEditorService(f.Connections, f.Factory, f.Profiles, new Lifetime());
        var opened = Json(await editor.Start(f.Session, f.Remote + "/edit.txt", CancellationToken.None, false));
        var local = opened.GetProperty("localPath").GetString()!;
        var id = opened.GetProperty("id").GetString()!;
        File.WriteAllText(local, "local saved");
        await Until(async () => await f.Read("edit.txt") == "local saved");
        Assert.Equal("640", (await f.Files.Stat(f.Remote + "/edit.txt", CancellationToken.None))!.Permissions);
        await f.Upload("edit.txt", "remote concurrent");
        File.WriteAllText(local, "unsaved conflict");
        await Until(() => Task.FromResult(Json(editor.List())[0].GetProperty("status").GetString() == "conflict"));
        Assert.Equal("remote concurrent", await f.Read("edit.txt"));
        await editor.Control(id, "overwrite");
        await Until(async () => await f.Read("edit.txt") == "unsaved conflict");
        await editor.Control(id, "stop");
        Assert.True(File.Exists(local));
    }

    [IntegrationFact]
    public async Task RemoteToolsCopySearchPermissionsSymlinkAndTrashRestore()
    {
        await using var f = await Fixture.Open();
        await f.Upload("a.txt", "한글 searchable");
        await f.Files.Copy(f.Remote + "/a.txt", f.Remote + "/b.txt", CancellationToken.None);
        Assert.Equal("한글 searchable", await f.Read("b.txt"));
        var search = await RemoteFileOperations.Search(f.Files, new(f.Remote, "*.txt", "searchable"), CancellationToken.None);
        Assert.Equal(2, search.Entries.Length);
        await f.Files.CreateDirectory(f.Remote + "/dir", CancellationToken.None);
        await f.Files.Move(f.Remote + "/b.txt", f.Remote + "/dir/b.txt", CancellationToken.None);
        await RemoteFileOperations.Permissions(f.Files, f.Remote + "/dir", "750", true, null, null, CancellationToken.None);
        Assert.Equal("750", (await f.Files.Stat(f.Remote + "/dir/b.txt", CancellationToken.None))!.Permissions);
        await f.Files.CreateLink(f.Remote + "/a.txt", f.Remote + "/link", CancellationToken.None);
        Assert.True((await f.Files.Stat(f.Remote + "/link", CancellationToken.None))!.IsLink);
        await f.Files.Move(f.Remote + "/link", f.Remote + "/renamed-link", CancellationToken.None);
        await f.Files.Delete(f.Remote + "/renamed-link", false, CancellationToken.None);
        Assert.Equal("한글 searchable", await f.Read("a.txt"));
        await f.Files.CreateLink(f.Remote + "/missing-target", f.Remote + "/dangling-link", CancellationToken.None);
        Assert.True((await f.Files.Stat(f.Remote + "/dangling-link", CancellationToken.None))!.IsLink);
        await f.Files.Delete(f.Remote + "/dangling-link", false, CancellationToken.None);
        var trash = new TrashService(f.Profiles, f.Connections);
        var entry = await trash.Put(f.Session, f.Remote + "/dir", CancellationToken.None);
        Assert.Null(await f.Files.Stat(f.Remote + "/dir", CancellationToken.None));
        await trash.Restore(entry.Id, f.Session, CancellationToken.None);
        Assert.Equal("한글 searchable", await f.Read("dir/b.txt"));
        var local = Path.Combine(f.Root, "local.txt");
        File.WriteAllText(local, "recover");
        var item = await trash.Put(null, local, CancellationToken.None);
        Assert.False(File.Exists(local));
        await trash.Restore(item.Id, null, CancellationToken.None);
        Assert.Equal("recover", File.ReadAllText(local));
    }

    [IntegrationFact]
    public async Task CustomCommandQuotesUntrustedFileNamesWithoutShellExpansion()
    {
        await using var f = await Fixture.Open();
        var path = "/file ' $(printf injected) {directory}.txt";
        var command = CustomCommands.Expand("printf '%s' {file}", "/do-not-expand", [path]);
        Assert.Equal(path, await f.Files.Command(command, CancellationToken.None));
    }

    [IntegrationFact]
    public async Task RealPtyShellSupportsInputResizeAndCtrlC()
    {
        await using var f = await Fixture.Open();
        await using var terminals = new TerminalService(f.Connections, new Interaction());
        var id = Json(await terminals.Open(f.Session, 100, 30, CancellationToken.None)).GetProperty("id").GetString()!;
        var output = "";
        terminals.Write(id, "stty size\r", 120, 42);
        await Until(() =>
        {
            output += Encoding.UTF8.GetString(Convert.FromBase64String(Json(terminals.Read(id)).GetProperty("data").GetString()!));
            return Task.FromResult(output.Contains("42 120"));
        });
        terminals.Write(id, "sleep 30\r", null, null);
        await Task.Delay(200);
        terminals.Write(id, "\u0003printf 'PTY-%s\\n' OK\r", null, null);
        output = "";
        await Until(() =>
        {
            output += Encoding.UTF8.GetString(Convert.FromBase64String(Json(terminals.Read(id)).GetProperty("data").GetString()!));
            return Task.FromResult(output.Contains("PTY-OK"));
        });
        await terminals.Close(id);
        Assert.Throws<KeyNotFoundException>(() => terminals.Read(id));
    }
}
