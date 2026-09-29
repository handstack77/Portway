using Portway.Core;

namespace Portway.Desktop;

public record TrashEntry(string Id, string Original, string Stored, bool Local, Site? Site, DateTimeOffset DeletedAt, string State = "ready");
public sealed class TrashService(ProfileStore profiles, Connections connections)
{
    readonly SemaphoreSlim gate = new(1, 1);
    public TrashEntry[] List() => profiles.ReadState<TrashEntry[]>("trash.json", []);
    void Save(IEnumerable<TrashEntry> entries) => profiles.WriteState("trash.json", entries.ToArray());
    public async Task<TrashEntry> Put(string? sessionId, string path, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var local = sessionId == null;
            var id = Guid.NewGuid().ToString("N");
            var original = local ? Path.GetFullPath(path) : RemotePaths.Normalize(path);
            var parent = local ? Path.GetDirectoryName(original) : RemotePaths.Parent(original);
            if (parent == null || original == "/" || local && string.Equals(original.TrimEnd(Path.DirectorySeparatorChar), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("루트 또는 홈 폴더는 휴지통으로 이동할 수 없습니다.");
            if (original.Split(local ? Path.DirectorySeparatorChar : '/').Contains(".portway-trash"))
                throw new IOException("휴지통 내부 항목은 다시 휴지통에 넣을 수 없습니다.");
            var directory = local ? Path.Combine(parent, ".portway-trash", id) : RemotePaths.Join(parent, ".portway-trash/" + id);
            var stored = local ? Path.Combine(directory, Path.GetFileName(original)) : RemotePaths.Join(directory, RemotePaths.Name(original));
            var entry = new TrashEntry(id, original, stored, local, local ? null : SiteSecrets.Public(connections.Get(sessionId!).Site), DateTimeOffset.UtcNow, "pending");
            // 이름 변경과 작업 기록 갱신 사이에 중단돼도 복구할 수 있도록 작업 의도를 먼저 저장합니다.
            Save(List().Append(entry));
            if (local)
            {
                var trashRoot = Path.Combine(parent, ".portway-trash");
                if (new DirectoryInfo(trashRoot).LinkTarget != null)
                    throw new IOException("휴지통 폴더가 심볼릭 링크입니다.");
                Directory.CreateDirectory(directory);
                LocalFiles.Rename(new(original, stored));
            }
            else
                await connections.Use(sessionId!, async fs =>
                {
                    await TransferOperations.EnsureDirectory(fs, directory, ct);
                    await fs.Move(original, stored, ct);
                    return true;
                }, ct);
            entry = entry with
            {
                State = "ready"
            };
            Save(List().Select(e => e.Id == id ? entry : e));
            return entry;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task Restore(string id, string? sessionId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var entry = List().Single(e => e.Id == id);
            if (entry.Local)
            {
                if (File.Exists(entry.Original) || Directory.Exists(entry.Original))
                    throw new IOException("원래 위치에 파일이 있습니다. 먼저 이름을 변경하세요.");
                LocalFiles.Rename(new(entry.Stored, entry.Original));
            }
            else
            {
                var site = connections.Get(sessionId ?? throw new ArgumentException("서버에 연결한 후 복원하세요.")).Site;
                if (!SiteSecrets.SameEndpoint(site, entry.Site!) || site.Fingerprint != entry.Site!.Fingerprint || site.TlsFingerprint != entry.Site.TlsFingerprint)
                    throw new IOException("원래 서버와 같은 검증된 연결을 선택하세요.");
                await connections.Use(sessionId, async fs =>
                {
                    if (await fs.Stat(entry.Original, ct) != null)
                        throw new IOException("원래 위치에 파일이 있습니다.");
                    await fs.Move(entry.Stored, entry.Original, ct);
                    return true;
                }, ct);
            }

            Save(List().Where(e => e.Id != id));
        }
        finally
        {
            gate.Release();
        }
    }
}
