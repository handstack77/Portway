using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Portway.Core;

namespace Portway.Desktop;

public record DropEntry(string Path, bool IsDirectory, long Size = 0, DateTimeOffset? Modified = null);
public record DropManifest(DropEntry[] Entries);
// WebView는 드롭한 파일의 운영체제 원본 경로를 의도적으로 공개하지 않습니다.
// 격리된 복사본을 디스크에 스트리밍한 뒤 영구 저장되는 전송 큐에 전달합니다.
public sealed class DropStore : BackgroundService
{
    public const int ChunkSize = 8 * 1024 * 1024;
    public const int MaxEntries = 20000;
    const long MaxBytes = 10L * 1024 * 1024 * 1024 * 1024;
    readonly string root;
    readonly string profileRoot;
    readonly TransferQueue queue;
    readonly ILogger<DropStore> log;
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    readonly object createGate = new();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    sealed record Record(DropEntry[] Entries, DateTimeOffset Created, bool Sealed = false);
    public DropStore(ProfileStore profiles, TransferQueue queue, ILogger<DropStore> log)
    {
        this.queue = queue;
        this.log = log;
        profileRoot = Path.GetFullPath(profiles.DataPath);
        root = Path.Combine(profileRoot, "drops");
        EnsureNoLinks(root);
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static void Validate(DropEntry[] entries)
    {
        if (entries.Length is 0 or > MaxEntries)
            throw new ArgumentException($"한 번에 1–{MaxEntries:N0}개 파일·폴더를 전송할 수 있습니다.");
        var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            var parts = entry.Path.Split('/');
            if (parts.Length > 64 || entry.Path.Length > 2000 || parts.Any(p => p.Length is 0 or > 240 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || "\\:*?\"<>|".Contains(c)) || IsDeviceName(p)))
                throw new ArgumentException("전송할 수 없는 경로입니다: " + entry.Path);
            if (!names.TryAdd(entry.Path.Normalize(NormalizationForm.FormC), entry.IsDirectory))
                throw new ArgumentException("대소문자 또는 유니코드 정규화가 같거나 중복되는 경로입니다: " + entry.Path);
            if (entry.Size < 0 || entry.Size > MaxBytes || (entry.IsDirectory && entry.Size != 0))
                throw new ArgumentException("파일 크기가 올바르지 않습니다.");
            total = checked(total + entry.Size);
            if (total > MaxBytes)
                throw new ArgumentException("드롭 1회의 크기는 10 TiB 이하여야 합니다.");
            if (entry.Modified is { } stamp && (stamp.Year < 1970 || stamp.Year > 9998))
                throw new ArgumentException("수정 시각이 올바르지 않습니다.");
        }

        foreach (var entry in entries)
        {
            var parent = entry.Path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (!names.TryGetValue(parent.Normalize(NormalizationForm.FormC), out var directory) || !directory)
                    throw new ArgumentException("상위 폴더 정보가 없거나 파일과 충돌합니다: " + entry.Path);
            }
        }

        if (entries.Count(e => !e.Path.Contains('/')) > 10000)
            throw new ArgumentException("최상위 항목은 10,000개 이하여야 합니다.");
    }

    static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]));
    }

    string DirectoryFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("잘못된 드롭 ID입니다.");
        var directory = Path.GetFullPath(Path.Combine(root, id));
        if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("저장 경로가 올바르지 않습니다.");
        EnsureNoLinks(directory);
        return directory;
    }

    static string Payload(string directory) => Path.Combine(directory, "files");
    string EntryPath(string directory, DropEntry entry)
    {
        var path = Path.GetFullPath(Path.Combine(Payload(directory), entry.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Payload(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("저장 범위를 벗어났습니다.");
        EnsureNoLinks(path);
        return path;
    }

    static Record Read(string directory) => JsonSerializer.Deserialize<Record>(File.ReadAllText(Path.Combine(directory, "manifest.json")), Json) ?? throw new IOException("드롭 정보를 읽을 수 없습니다.");
    void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget != null || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("드롭 저장 경로에 링크를 사용할 수 없습니다.");
            // 앱 전용 프로필 상위의 운영체제 경로 별칭(예: macOS /var → /private/var)은
            // 허용합니다. 전용 프로필 내부의 모든 경로 구성 요소는 검사합니다.
            if (string.Equals(current, profileRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                break;
        }
    }

    static void Save(string directory, Record record)
    {
        var file = Path.Combine(directory, "manifest.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(record, Json));
        File.Move(file + ".tmp", file, true);
    }

    public object Create(DropManifest manifest)
    {
        Validate(manifest.Entries);
        lock (createGate)
        {
            if (Directory.EnumerateDirectories(root).Count() >= 100)
                throw new InvalidOperationException("준비 중인 외부 전송이 너무 많습니다. 기존 전송을 마치세요.");
            var required = manifest.Entries.Sum(e => e.Size);
            var disk = new DriveInfo(Path.GetPathRoot(root)!);
            if (disk.AvailableFreeSpace < required + 64L * 1024 * 1024)
                throw new IOException("전송 준비에 필요한 임시 디스크 공간이 부족합니다.");
            var id = Guid.NewGuid().ToString("N");
            var directory = DirectoryFor(id);
            Directory.CreateDirectory(Payload(directory));
            try
            {
                foreach (var entry in manifest.Entries.OrderBy(e => e.Path.Length))
                {
                    var path = EntryPath(directory, entry);
                    if (entry.IsDirectory)
                        Directory.CreateDirectory(path);
                    else
                        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        {
                        }
                }

                Save(directory, new(manifest.Entries, DateTimeOffset.UtcNow));
                return new
                {
                    id,
                    chunkSize = ChunkSize
                };
            }
            catch
            {
                DeleteOwned(directory);
                throw;
            }
        }
    }

    public async Task<long> Append(string id, int index, long offset, long length, Stream body, CancellationToken ct)
    {
        var gate = gates.GetOrAdd(id, _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            var directory = DirectoryFor(id);
            var record = Read(directory);
            if (record.Sealed || queue.FindSourceJob(Payload(directory)) != null)
                throw new InvalidOperationException("이미 전송 큐에 등록된 드롭입니다.");
            if (index < 0 || index >= record.Entries.Length)
                throw new ArgumentException("잘못된 파일 번호입니다.");
            var entry = record.Entries[index];
            if (entry.IsDirectory || length is <= 0 or > ChunkSize || offset < 0 || offset > entry.Size - length)
                throw new ArgumentException("청크 범위가 올바르지 않습니다.");
            await using var stream = new FileStream(EntryPath(directory, entry), FileMode.Open, FileAccess.Write, FileShare.None, 65536, true);
            if (stream.Length != offset)
                throw new InvalidOperationException("청크 위치가 일치하지 않습니다. 파일을 다시 드롭하세요.");
            stream.Position = offset;
            try
            {
                var buffer = new byte[65536];
                long remaining = length;
                while (remaining > 0)
                {
                    var count = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                    if (count == 0)
                        throw new EndOfStreamException("파일 수신이 중단되었습니다.");
                    await stream.WriteAsync(buffer.AsMemory(0, count), ct);
                    remaining -= count;
                }

                if (await body.ReadAsync(buffer.AsMemory(0, 1), ct) != 0)
                    throw new IOException("청크 크기를 초과했습니다.");
                await stream.FlushAsync(ct);
                Save(directory, record with { Created = DateTimeOffset.UtcNow });
            }
            catch
            {
                stream.SetLength(offset);
                throw;
            }

            return stream.Length;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> Commit(string id, TransferRequest request, CancellationToken ct)
    {
        var gate = gates.GetOrAdd(id, _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            var directory = DirectoryFor(id);
            var record = Read(directory);
            var existing = queue.FindSourceJob(Payload(directory));
            if (existing != null)
                return existing.Value.Id; // HTTP 응답이 유실돼도 전송 큐에 중복 등록하지 않습니다.
            if (request.Direction != "upload" || request.Options?.RemoveSource == true)
                throw new ArgumentException("외부 드롭은 원본을 유지하는 업로드입니다.");
            foreach (var entry in record.Entries.OrderByDescending(e => e.Path.Length))
            {
                var path = EntryPath(directory, entry);
                if (!entry.IsDirectory && new FileInfo(path).Length != entry.Size)
                    throw new InvalidOperationException("파일 수신이 완료되지 않았습니다: " + entry.Path);
                if (entry.Modified is { } stamp)
                {
                    if (entry.IsDirectory)
                        Directory.SetLastWriteTimeUtc(path, stamp.UtcDateTime);
                    else
                        File.SetLastWriteTimeUtc(path, stamp.UtcDateTime);
                }
            }

            Save(directory, record with { Sealed = true });
            return queue.Add(request with { Paths = record.Entries.Where(e => !e.Path.Contains('/')).Select(e => EntryPath(directory, e)).ToArray(), Options = (request.Options ?? new()) with { RemoveSource = false } });
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task Abandon(string id, CancellationToken ct)
    {
        var gate = gates.GetOrAdd(id, _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            var directory = DirectoryFor(id);
            if (queue.FindSourceJob(Payload(directory)) != null)
                throw new InvalidOperationException("전송 큐의 원본은 삭제할 수 없습니다. 큐에서 작업을 취소하세요.");
            DeleteOwned(directory);
        }
        finally
        {
            gate.Release();
        }
    }

    void DeleteOwned(string directory)
    {
        // 하위 항목을 포함해 재분석 지점을 통과하는 재귀 삭제는 수행하지 않습니다.
        var verified = DirectoryFor(Path.GetFileName(directory));
        if (!Directory.Exists(verified))
            return;
        DeleteTree(verified);
    }

    void DeleteTree(string directory)
    {
        EnsureNoLinks(directory);
        foreach (var file in Directory.EnumerateFileSystemEntries(directory))
        {
            EnsureNoLinks(file);
            if (Directory.Exists(file))
                DeleteTree(file);
            else
                File.Delete(file);
        }

        Directory.Delete(directory);
    }

    public async Task Sweep(CancellationToken ct)
    {
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _))
                continue;
            var gate = gates.GetOrAdd(id, _ => new(1));
            if (!await gate.WaitAsync(0, ct))
                continue;
            try
            {
                var verified = DirectoryFor(id);
                var job = queue.FindSourceJob(Payload(verified));
                if (job is { Status: not "completed" })
                    continue; // 일시정지·실패·취소 작업은 다시 시작한 뒤에도 재시도할 수 있도록 보존합니다.
                var created = File.Exists(Path.Combine(verified, "manifest.json")) ? Read(verified).Created : Directory.GetCreationTimeUtc(verified);
                if (job?.Status == "completed" || created < DateTimeOffset.UtcNow.AddDays(-1))
                    DeleteOwned(verified);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                log.LogWarning(ex, "외부 드롭 임시 파일을 정리하지 못했습니다. 드롭 ID: {DropId}", id);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            await Sweep(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
