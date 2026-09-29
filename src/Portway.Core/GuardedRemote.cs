namespace Portway.Core;
// 원격 서버가 제공한 디렉토리 이름은 신뢰할 수 없는 입력으로 취급합니다.
public sealed class GuardedRemote(IRemoteFileSystem inner) : IRemoteFileSystem
{
    public Capabilities Capabilities => inner.Capabilities;

    public Task Connect(CancellationToken ct) => inner.Connect(ct);
    public async Task<Entry[]> List(string path, CancellationToken ct)
    {
        var entries = await inner.List(RemotePaths.Normalize(path), ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (e.Name is "" or "." or ".." || e.Name.Contains('/') || RemotePaths.Normalize(e.Path) != RemotePaths.Join(path, e.Name) || !seen.Add(e.Name))
                throw new IOException("원격 서버가 안전하지 않은 디렉터리 목록을 반환했습니다.");
        }

        return entries;
    }

    public Task<Entry?> Stat(string path, CancellationToken ct) => inner.Stat(RemotePaths.Normalize(path), ct);
    public Task CreateDirectory(string path, CancellationToken ct) => inner.CreateDirectory(RemotePaths.Normalize(path), ct);
    public Task Delete(string path, bool directory, CancellationToken ct) => inner.Delete(RemotePaths.Normalize(path), directory, ct);
    public Task Move(string source, string destination, CancellationToken ct)
    {
        source = RemotePaths.Normalize(source);
        destination = RemotePaths.Normalize(destination);
        if (source == "/" || destination == "/" || destination.StartsWith(source + "/", StringComparison.Ordinal))
            throw new IOException("루트 이동 또는 하위 폴더로의 이동은 허용하지 않습니다.");
        return inner.Move(source, destination, ct);
    }

    public Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct) => inner.Download(RemotePaths.Normalize(remote), local, offset, progress, ct);
    public Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct) => inner.Upload(local, RemotePaths.Normalize(remote), offset, progress, ct);
    public Task Chmod(string path, string octal, CancellationToken ct) => inner.Chmod(RemotePaths.Normalize(path), octal, ct);
    public Task SetModified(string path, DateTimeOffset modified, CancellationToken ct) => inner.SetModified(RemotePaths.Normalize(path), modified, ct);
    public Task CreateLink(string target, string link, CancellationToken ct) => inner.CreateLink(target, RemotePaths.Normalize(link), ct);
    public Task SetOwner(string path, int? owner, int? group, CancellationToken ct) => inner.SetOwner(RemotePaths.Normalize(path), owner, group, ct);
    public Task Copy(string source, string destination, CancellationToken ct)
    {
        source = RemotePaths.Normalize(source);
        destination = RemotePaths.Normalize(destination);
        if (source == destination || destination.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new IOException("원본 내부로 복사할 수 없습니다.");
        return inner.Copy(source, destination, ct);
    }

    public Task<string> Command(string command, CancellationToken ct) => inner.Command(command, ct);
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
