namespace Portway.Core.Automation;
/// <summary>여러 운영체제를 지원하는 프로세스 내부 자동화입니다. 각 세션의 작업은 순차적으로 실행합니다.</summary>
public sealed class Session(IAuthenticationInteraction? interaction = null) : IAsyncDisposable
{
    readonly SemaphoreSlim gate = new(1, 1);
    IRemoteFileSystem? remote;
    public event Action<string, TransferProgress>? Progress;
    IRemoteFileSystem Files => remote ?? throw new InvalidOperationException("먼저 세션을 연결하세요.");

    public async Task Open(Site site, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (remote != null)
                throw new InvalidOperationException("세션이 이미 연결되어 있습니다.");
            var candidate = new RemoteFactory(interaction).Create(site);
            try
            {
                await candidate.Connect(ct);
                remote = candidate;
            }
            catch
            {
                await candidate.DisposeAsync();
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T> Use<T>(Func<IRemoteFileSystem, Task<T>> operation, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await operation(Files);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<Entry[]> ListDirectory(string path, CancellationToken ct = default) => Use(fs => fs.List(path, ct), ct);
    public Task<Entry?> GetFileInfo(string path, CancellationToken ct = default) => Use(fs => fs.Stat(path, ct), ct);
    public Task<string> CalculateFileChecksum(string path, string algorithm = "sha256", CancellationToken ct = default) => Use(fs => FileChecksums.Remote(fs, path, algorithm, ct), ct);
    public Task<string> ExecuteCommand(string command, CancellationToken ct = default) => Use(fs => fs.Command(command, ct), ct);
    public Task PutFiles(IEnumerable<string> local, string remoteDirectory, TransferOptions? options = null, string conflict = "replace", CancellationToken ct = default) => Transfer("upload", local, remoteDirectory, options, conflict, ct);
    public Task GetFiles(IEnumerable<string> remotePaths, string localDirectory, TransferOptions? options = null, string conflict = "replace", CancellationToken ct = default) => Transfer("download", remotePaths, localDirectory, options, conflict, ct);
    Task Transfer(string direction, IEnumerable<string> paths, string destination, TransferOptions? options, string conflict, CancellationToken ct) => Use(async fs =>
    {
        var id = Guid.NewGuid().ToString("N");
        foreach (var path in paths)
            await TransferOperations.Transfer(fs, direction, path, destination, conflict, id, (p, n) => Progress?.Invoke(p, n), ct, options: options);
        return true;
    }, ct);
    public Task CreateDirectory(string path, CancellationToken ct = default) => Use(async fs =>
    {
        await TransferOperations.EnsureDirectory(fs, path, ct);
        return true;
    }, ct);
    public Task RemoveFiles(IEnumerable<string> paths, CancellationToken ct = default) => Use(async fs =>
    {
        foreach (var path in paths)
            await TransferOperations.DeleteTree(fs, path, ct);
        return true;
    }, ct);
    public Task MoveFile(string source, string destination, CancellationToken ct = default) => Use(async fs =>
    {
        await fs.Move(source, destination, ct);
        return true;
    }, ct);
    public Task DuplicateFile(string source, string destination, CancellationToken ct = default) => Use(async fs =>
    {
        await fs.Copy(source, destination, ct);
        return true;
    }, ct);
    public Task CreateSymbolicLink(string target, string link, CancellationToken ct = default) => Use(async fs =>
    {
        await fs.CreateLink(target, link, ct);
        return true;
    }, ct);
    public Task<SyncPlan> CompareDirectories(SyncRequest request, CancellationToken ct = default) => Use(fs => Synchronizer.Preview(fs, request, ct), ct);
    public Task SynchronizeDirectories(SyncRequest request, CancellationToken ct = default) => Use(async fs =>
    {
        var plan = await Synchronizer.Preview(fs, request, ct);
        await Synchronizer.Apply(fs, request, plan, _ =>
        {
        }, ct);
        return true;
    }, ct);
    public async Task Close()
    {
        await gate.WaitAsync();
        try
        {
            if (remote != null)
            {
                await remote.DisposeAsync();
                remote = null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await Close();
}
