namespace Portway.Core;
// 기존 다운로드 엔진을 로컬 폴더 간 전송에도 사용하며 원격 연결을 만들지 않습니다.
public sealed class LocalTransferFileSystem : IRemoteFileSystem
{
    public Capabilities Capabilities { get; } = new(true, false, false, true, true);
    static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static bool Within(string path, string root) => path.Equals(root, Comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, Comparison);
    public static string SafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("로컬 전송에는 절대 경로가 필요합니다.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (var current = full; current != null; current = Path.GetDirectoryName(current))
        {
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null || info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("심볼릭 링크 또는 재분석 지점을 통한 로컬 전송은 허용하지 않습니다: " + current);
        }

        return full;
    }

    public static TransferRequest Validate(TransferRequest request)
    {
        var destination = SafePath(request.Destination);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException("대상 폴더가 없습니다: " + destination);
        var options = request.Options ?? new();
        // 컴퓨터 안에서 복사하는 파일의 줄바꿈과 내용은 그대로 보존합니다.
        options = options with
        {
            Mode = "binary",
            Permissions = null
        };
        options.Validate();
        var paths = request.Paths.Select(SafePath).ToArray();
        var targets = new HashSet<string>(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (Path.GetPathRoot(path) == path)
                throw new IOException("드라이브 또는 파일 시스템 루트는 전송할 수 없습니다.");
            var source = LocalFiles.Stat(path);
            var target = SafePath(RemotePaths.SafeLocalChild(destination, options.TargetName(source.Name)));
            if (path.Equals(target, Comparison) || source.IsDirectory && (Within(target, path) || Within(path, target)))
                throw new IOException("같은 경로 또는 서로 포함하는 폴더 사이로 전송할 수 없습니다.");
            if (!targets.Add(target))
                throw new IOException("선택 항목의 대상 이름이 충돌합니다.");
            if (paths.Any(other => !path.Equals(other, Comparison) && Within(other, path)))
                throw new IOException("폴더와 그 하위 항목을 중복으로 전송할 수 없습니다.");
        }

        return request with
        {
            SessionId = "",
            Paths = paths,
            Destination = destination,
            Options = options
        };
    }

    public Task Connect(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<Entry[]> List(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(LocalFiles.List(SafePath(path)).Entries);
    }

    public Task<Entry?> Stat(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        path = SafePath(path);
        return Task.FromResult<Entry?>(File.Exists(path) || Directory.Exists(path) ? LocalFiles.Stat(path) with { Permissions = File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly) ? "444" : "644" } : null);
    }

    public Task Delete(string path, bool directory, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        path = SafePath(path);
        if (directory)
        {
            if (path == Path.GetPathRoot(path) || path.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Comparison))
                throw new IOException("루트 또는 홈 폴더는 삭제할 수 없습니다.");
            Directory.Delete(path, false);
        }
        else
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task Download(string source, string destination, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        source = SafePath(source);
        destination = SafePath(destination);
        if (source.Equals(destination, Comparison))
            throw new IOException("원본 파일에 덮어쓸 수 없습니다.");
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous);
        if (offset < 0 || offset > input.Length || output.Length < offset)
            throw new IOException("이어하기 위치가 올바르지 않습니다.");
        input.Position = offset;
        output.SetLength(offset);
        output.Position = offset;
        var buffer = new byte[131072];
        long bytes = offset;
        int read;
        progress(new(bytes, input.Length));
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            bytes += read;
            progress(new(bytes, input.Length));
        }

        await output.FlushAsync(ct);
    }

    public Task CreateDirectory(string path, CancellationToken ct) => throw new NotSupportedException("로컬 전송 대상 폴더는 전송 엔진에서 생성합니다.");
    public Task Move(string source, string destination, CancellationToken ct) => throw new NotSupportedException();
    public Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
