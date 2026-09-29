using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Portway.Core;

public record SyncChange(string Action, string RelativePath, long Size, string Reason, string? LocalPath = null, string? RemotePath = null);
public record SyncPlan(SyncChange[] Changes, string Snapshot);
/// <summary>로컬·원격 비교 계획과 사용자가 선택한 변경의 적용을 담당합니다.</summary>
public static class Synchronizer
{
    public static async Task<SyncPlan> Preview(IRemoteFileSystem remote, SyncRequest request, CancellationToken ct)
    {
        if (request.Direction is not ("upload" or "download" or "both"))
            throw new ArgumentException("동기화 방향이 올바르지 않습니다.");
        if (request.Direction == "both" && request.DeleteExtraneous)
            throw new ArgumentException("양방향 동기화에서는 삭제할 수 없습니다.");
        if (request.Comparison is not ("checksum" or "time-size" or "size" or "time"))
            throw new ArgumentException("비교 기준이 올바르지 않습니다.");
        var options = request.Options ?? new();
        options.Validate();
        if (options.RemoveSource)
            throw new ArgumentException("동기화에서는 전송 후 원본 삭제를 사용할 수 없습니다.");
        var mask = new FileMask(options.FileMask);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var local = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        var distant = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        string Logical(string path) => string.Join('/', path.Split('/').Select(options.TargetName));
        bool Included(Entry entry, string relative) => !entry.Name.Contains(".portway-", StringComparison.Ordinal) && entry.Name != ".portway-trash" && mask.Matches(entry, relative) && (!options.ExcludeHidden || !entry.Name.StartsWith('.'));
        void ScanLocal(string path, string prefix, int depth)
        {
            if (depth > 64 || local.Count > 20000)
                throw new IOException("동기화는 최대 20,000개 항목, 깊이 64를 지원합니다.");
            foreach (var e in LocalFiles.List(path).Entries)
            {
                ct.ThrowIfCancellationRequested();
                var rel = prefix + e.Name;
                if (!Included(e, rel))
                    continue;
                if (e.IsLink)
                    throw new IOException("동기화 폴더에 심볼릭 링크가 있습니다: " + e.Path);
                if (!local.TryAdd(Logical(rel), e))
                    throw new IOException("대소문자 변환 후 로컬 이름이 충돌합니다: " + rel);
                if (e.IsDirectory)
                    ScanLocal(e.Path, rel + "/", depth + 1);
            }
        }

        async Task ScanRemote(string path, string prefix, int depth)
        {
            if (depth > 64 || distant.Count > 20000)
                throw new IOException("원격 폴더가 동기화 한도를 초과했습니다.");
            foreach (var e in await remote.List(path, ct))
            {
                var rel = prefix + e.Name;
                if (!Included(e, rel))
                    continue;
                if (e.IsLink)
                    throw new IOException("동기화 폴더에 심볼릭 링크가 있습니다: " + e.Path);
                RemotePaths.SafeLocalChild(request.LocalPath, e.Name);
                if (!distant.TryAdd(Logical(rel), e))
                    throw new IOException("대소문자 변환 후 원격 이름이 충돌합니다: " + rel);
                if (e.IsDirectory)
                    await ScanRemote(e.Path, rel + "/", depth + 1);
            }
        }

        ScanLocal(request.LocalPath, "", 0);
        await ScanRemote(request.RemotePath, "", 0);
        async Task<string> LocalHash(Entry entry)
        {
            var key = "L:" + entry.Path;
            if (!hashes.TryGetValue(key, out var hash))
                hashes[key] = hash = await FileChecksums.Local(entry.Path, "sha256", ct);
            return hash;
        }

        async Task<string> RemoteHash(Entry entry)
        {
            var key = "R:" + entry.Path;
            if (!hashes.TryGetValue(key, out var hash))
                hashes[key] = hash = await FileChecksums.Remote(remote, entry.Path, "sha256", ct);
            return hash;
        }

        var changes = new List<SyncChange>();
        foreach (var name in local.Keys.Union(distant.Keys).Order(StringComparer.Ordinal))
        {
            local.TryGetValue(name, out var l);
            distant.TryGetValue(name, out var r);
            if (l != null && r != null && l.IsDirectory != r.IsDirectory)
                throw new IOException("파일/폴더 이름이 충돌합니다: " + name);
            if (l?.IsDirectory == true || r?.IsDirectory == true)
            {
                if (options.ExcludeEmptyDirectories && !local.Any(e => e.Key.StartsWith(name + "/", StringComparison.Ordinal) && !e.Value.IsDirectory) && !distant.Any(e => e.Key.StartsWith(name + "/", StringComparison.Ordinal) && !e.Value.IsDirectory))
                    continue;
                if (l == null)
                    changes.Add(new(request.Direction is "download" or "both" ? "mkdir-local" : request.DeleteExtraneous ? "delete-remote-dir" : "ignore", name, 0, "폴더"));
                else if (r == null)
                    changes.Add(new(request.Direction is "upload" or "both" ? "mkdir-remote" : request.DeleteExtraneous ? "delete-local-dir" : "ignore", name, 0, "폴더"));
                continue;
            }

            if (l == null)
                changes.Add(new(request.Direction is "download" or "both" ? "download" : request.DeleteExtraneous ? "delete-remote" : "ignore", name, r!.Size, "로컬에 없음"));
            else if (r == null)
                changes.Add(new(request.Direction is "upload" or "both" ? "upload" : request.DeleteExtraneous ? "delete-local" : "ignore", name, l.Size, "원격에 없음"));
            else
            {
                var equal = request.Comparison switch
                {
                    "size" => l.Size == r.Size,
                    "time" => Math.Abs((l.Modified - r.Modified).TotalSeconds) < 2,
                    "time-size" => l.Size == r.Size && Math.Abs((l.Modified - r.Modified).TotalSeconds) < 2,
                    _ => false
                };
                // 내용을 비교해 WebDAV/S3 서버의 수정 시각 때문에 업로드가 반복되지 않도록 합니다.
                var isText = options.Mode == "text" || options.Mode == "automatic" && new FileMask(options.TextMask).Matches(l, name);
                if (request.Comparison == "checksum" && (l.Size == r.Size || isText))
                {
                    if (!isText)
                        equal = await LocalHash(l) == await RemoteHash(r);
                    else
                    {
                        var temp = Path.GetTempFileName();
                        var a = Path.GetTempFileName();
                        var b = Path.GetTempFileName();
                        try
                        {
                            await remote.Download(r.Path, temp, 0, _ =>
                            {
                            }, ct);
                            await TextTransfer.ConvertNewlines(l.Path, a, false, ct);
                            await TextTransfer.ConvertNewlines(temp, b, false, ct);
                            equal = await FileChecksums.Local(a, "sha256", ct) == await FileChecksums.Local(b, "sha256", ct);
                            hashes["L:" + l.Path] = await FileChecksums.Local(l.Path, "sha256", ct);
                            hashes["R:" + r.Path] = await FileChecksums.Local(temp, "sha256", ct);
                        }
                        finally
                        {
                            File.Delete(temp);
                            File.Delete(a);
                            File.Delete(b);
                        }
                    }
                }

                if (!equal)
                {
                    var action = request.Direction == "both" ? Math.Abs((l.Modified - r.Modified).TotalSeconds) < 2 ? "conflict" : l.Modified > r.Modified ? "upload" : "download" : request.Direction;
                    changes.Add(new(action, name, action == "download" ? r.Size : l.Size, action == "conflict" ? "같은 수정 시각, 다른 내용 — 수동 확인 필요" : "내용이 다름"));
                }
            }
        }

        var result = changes.Where(c => c.Action != "ignore").Select(c => c with { LocalPath = local.GetValueOrDefault(c.RelativePath)?.Path, RemotePath = distant.GetValueOrDefault(c.RelativePath)?.Path }).ToArray();
        // 크기가 다른 파일도 포함해 변경 예정인 모든 파일의 지문을 계산합니다.
        // 파일 길이와 수정 시각을 유지한 수정은 메타데이터만으로 감지할 수 없습니다.
        foreach (var change in result)
        {
            if (local.TryGetValue(change.RelativePath, out var l) && !l.IsDirectory)
                await LocalHash(l);
            if (distant.TryGetValue(change.RelativePath, out var r) && !r.IsDirectory)
                await RemoteHash(r);
        }

        var snapshot = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { local, distant, changes, hashes }))));
        return new(result, snapshot);
    }

    public static async Task Apply(IRemoteFileSystem remote, SyncRequest request, SyncPlan plan, Action<string> progress, CancellationToken ct, string[]? selected = null, IReadOnlyDictionary<string, string>? resolutions = null)
    {
        var current = await Preview(remote, request, ct);
        if (current.Snapshot != plan.Snapshot)
            throw new IOException("미리보기 이후 파일이 변경되었습니다. 다시 비교하세요.");
        var chosen = plan.Changes.Where(c => selected == null || selected.Contains(c.RelativePath, StringComparer.Ordinal)).Select(c => c.Action == "conflict" && resolutions?.TryGetValue(c.RelativePath, out var action) == true && action is "upload" or "download" or "ignore" ? c with { Action = action } : c).Where(c => c.Action != "ignore").ToArray();
        if (chosen.Any(c => c.Action == "conflict"))
            throw new IOException("선택한 충돌의 처리 방향을 정하세요.");
        var operation = Guid.NewGuid().ToString("N");
        var changes = chosen.Where(c => !c.Action.StartsWith("delete")).OrderBy(c => c.RelativePath.Count(x => x == '/')).Concat(chosen.Where(c => c.Action.StartsWith("delete")).OrderByDescending(c => c.RelativePath.Count(x => x == '/')).ThenBy(c => c.Action.EndsWith("dir")));
        foreach (var c in changes)
        {
            ct.ThrowIfCancellationRequested();
            progress(c.RelativePath);
            var local = c.RelativePath.Split('/').Aggregate(request.LocalPath, RemotePaths.SafeLocalChild);
            var distant = RemotePaths.Join(request.RemotePath, c.RelativePath);
            switch (c.Action)
            {
                case "mkdir-local":
                    Directory.CreateDirectory(local);
                    break;
                case "mkdir-remote":
                    await TransferOperations.EnsureDirectory(remote, distant, ct);
                    break;
                case "upload":
                    await TransferOperations.Transfer(remote, "upload", c.LocalPath ?? local, RemotePaths.Parent(distant)!, "replace", operation, (_, _) =>
                    {
                    }, ct, options: request.Options, prefix: c.RelativePath.Contains('/') ? c.RelativePath[..(c.RelativePath.LastIndexOf('/') + 1)] : "");
                    break;
                case "download":
                    await TransferOperations.Transfer(remote, "download", c.RemotePath ?? distant, Path.GetDirectoryName(local)!, "replace", operation, (_, _) =>
                    {
                    }, ct, options: request.Options, prefix: c.RelativePath.Contains('/') ? c.RelativePath[..(c.RelativePath.LastIndexOf('/') + 1)] : "");
                    break;
                case "delete-local":
                    File.Delete(c.LocalPath ?? local);
                    break;
                case "delete-local-dir":
                    if (!Directory.EnumerateFileSystemEntries(c.LocalPath ?? local).Any())
                        Directory.Delete(c.LocalPath ?? local, false);
                    break;
                case "delete-remote":
                    await remote.Delete(c.RemotePath ?? distant, false, ct);
                    break;
                case "delete-remote-dir":
                    if ((await remote.List(c.RemotePath ?? distant, ct)).Length == 0)
                        await remote.Delete(c.RemotePath ?? distant, true, ct);
                    break;
            }
        }
    }
}
