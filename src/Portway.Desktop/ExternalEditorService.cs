using System.Collections.Concurrent;
using System.Diagnostics;
using Portway.Core;

namespace Portway.Desktop;

public sealed class ExternalEditorService(Connections connections, RemoteFactory factory, ProfileStore profiles, IHostApplicationLifetime lifetime) : IAsyncDisposable
{
    sealed class Edit(string id, Site site, string remote, string local, string hash)
    {
        public string Id = id;
        public Site Site = site;
        public string Remote = remote, Local = local, RemoteHash = hash, LocalHash = hash;
        public string Status = "watching";
        public string? Error;
        public bool Force;
        public CancellationTokenSource Cancel = new();
        public Task? Task;
        public Process? Process;
    }

    readonly ConcurrentDictionary<string, Edit> edits = new();
    public object[] List() => edits.Values.Select(e =>
    {
        lock (e)
            return (object)new
            {
                e.Id,
                e.Remote,
                e.Local,
                e.Status,
                e.Error,
                site = e.Site.Name
            };
    }).ToArray();
    public bool Active => !edits.IsEmpty;

    public async Task<object> Start(string sessionId, string remote, CancellationToken ct, bool launch = true)
    {
        if (edits.Count >= 20)
            throw new InvalidOperationException("외부 편집 세션은 최대 20개입니다.");
        var prefs = profiles.Preferences();
        if (launch && string.IsNullOrWhiteSpace(prefs.EditorExecutable))
            throw new InvalidOperationException("설정에서 외부 편집기 실행 파일을 지정하세요.");
        var site = connections.Get(sessionId).Site;
        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(profiles.DataPath, "editor", id);
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var local = RemotePaths.SafeLocalChild(dir, RemotePaths.Name(remote));
        await using var fs = factory.Create(site);
        await fs.Connect(ct);
        var info = await fs.Stat(remote, ct) ?? throw new FileNotFoundException(remote);
        if (info.IsDirectory || info.IsLink)
            throw new IOException("일반 파일을 선택하세요.");
        await fs.Download(remote, local, 0, _ =>
        {
        }, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(local, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var hash = await FileChecksums.Local(local, "sha256", ct);
        var edit = new Edit(id, site, remote, local, hash);
        if (launch)
        {
            var start = new ProcessStartInfo(prefs.EditorExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = dir
            };
            var args = prefs.EditorArguments is { Length: > 0 } ? prefs.EditorArguments : ["{file}"];
            foreach (var argument in args)
                start.ArgumentList.Add(argument.Replace("{file}", local, StringComparison.Ordinal));
            edit.Process = Process.Start(start) ?? throw new IOException("편집기를 시작할 수 없습니다.");
        }

        edits[id] = edit;
        edit.Task = Task.Run(() => Watch(edit));
        return new
        {
            id,
            localPath = local
        };
    }

    async Task Watch(Edit edit)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(edit.Cancel.Token, lifetime.ApplicationStopping);
        var ct = linked.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(750, ct);
                try
                {
                    if (!File.Exists(edit.Local))
                        continue;
                    var lastWrite = File.GetLastWriteTimeUtc(edit.Local);
                    var length = new FileInfo(edit.Local).Length;
                    await Task.Delay(500, ct);
                    if (!File.Exists(edit.Local) || File.GetLastWriteTimeUtc(edit.Local) != lastWrite || new FileInfo(edit.Local).Length != length)
                        continue;
                    var hash = await FileChecksums.Local(edit.Local, "sha256", ct);
                    bool force;
                    lock (edit)
                        force = edit.Force;
                    if (hash == edit.LocalHash && !force)
                        continue;
                    await using var remote = factory.Create(edit.Site);
                    await remote.Connect(ct);
                    var original = await FileChecksums.Remote(remote, edit.Remote, "sha256", ct);
                    if (!force && original != edit.RemoteHash)
                    {
                        lock (edit)
                        {
                            edit.Status = "conflict";
                            edit.Error = "서버 파일도 변경되었습니다. 로컬 사본을 보존했습니다.";
                        }

                        continue;
                    }

                    lock (edit)
                    {
                        edit.Status = "saving";
                        edit.Error = null;
                    }

                    var temporary = edit.Remote + ".portway-edit-" + edit.Id;
                    await remote.Upload(edit.Local, temporary, 0, _ =>
                    {
                    }, ct);
                    if (hash != await FileChecksums.Local(edit.Local, "sha256", ct))
                        continue;
                    if (await FileChecksums.Remote(remote, edit.Remote, "sha256", ct) != original)
                    {
                        lock (edit)
                        {
                            edit.Status = "conflict";
                            edit.Error = "저장 중 서버 파일이 변경되었습니다.";
                        }

                        continue;
                    }

                    if (hash != await FileChecksums.Remote(remote, temporary, "sha256", ct))
                        throw new IOException("편집 파일 전송 검증에 실패했습니다.");
                    await TransferOperations.Commit(remote, temporary, edit.Remote, edit.Id, ct);
                    lock (edit)
                    {
                        edit.RemoteHash = hash;
                        edit.LocalHash = hash;
                        edit.Force = false;
                        edit.Status = "watching";
                        edit.Error = null;
                    }
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    lock (edit)
                    {
                        edit.Status = "error";
                        edit.Error = e.Message;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            lock (edit)
                if (edit.Status != "conflict")
                    edit.Status = "stopped";
        }
    }

    public async Task Control(string id, string action)
    {
        if (!edits.TryGetValue(id, out var edit))
            throw new KeyNotFoundException();
        if (action == "overwrite")
        {
            lock (edit)
                edit.Force = true;
            return;
        }

        if (action != "stop")
            throw new ArgumentException("편집 작업이 올바르지 않습니다.");
        edit.Cancel.Cancel();
        if (edit.Task != null)
            await edit.Task;
        edits.TryRemove(id, out _);
        edit.Cancel.Dispose();
        edit.Process?.Dispose();
        // 로컬 복사본은 복구용으로 보관하며 저장하지 않은 사용자 수정본을 자동으로 삭제하지 않습니다.
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in edits.Keys)
            await Control(id, "stop");
    }
}
