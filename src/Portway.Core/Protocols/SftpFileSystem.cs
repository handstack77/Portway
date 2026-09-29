using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Portway.Core.Protocols;

public sealed class SftpFileSystem(Site site, IAuthenticationInteraction? interaction = null) : IRemoteFileSystem
{
    readonly SftpClient client = SshConnection.Verify(new SftpClient(SshConnection.Info(site, interaction)), site);
    public Capabilities Capabilities => new(true, true, true, true, true, true);

    public Task Connect(CancellationToken ct) => client.ConnectAsync(ct);
    static Entry Map(ISftpFile f) => new(f.Name, f.FullName, f.IsDirectory, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), Mode(f.Attributes), f.IsSymbolicLink, f.Attributes.UserId.ToString(), f.Attributes.GroupId.ToString());
    static string Mode(SftpFileAttributes a)
    {
        var special = (a.IsUIDBitSet ? 4 : 0) + (a.IsGroupIDBitSet ? 2 : 0) + (a.IsStickyBitSet ? 1 : 0);
        return (special == 0 ? "" : special.ToString()) + $"{(a.OwnerCanRead ? 4 : 0) + (a.OwnerCanWrite ? 2 : 0) + (a.OwnerCanExecute ? 1 : 0)}{(a.GroupCanRead ? 4 : 0) + (a.GroupCanWrite ? 2 : 0) + (a.GroupCanExecute ? 1 : 0)}{(a.OthersCanRead ? 4 : 0) + (a.OthersCanWrite ? 2 : 0) + (a.OthersCanExecute ? 1 : 0)}";
    }

    public Task<Entry[]> List(string path, CancellationToken ct) => Task.Run(() => client.ListDirectory(path).Where(f => f.Name is not "." and not "..").Select(Map).ToArray(), ct);
    // Get/DeleteFile/RenameFile은 마지막 경로를 정규화하며 심볼릭 링크를 따라갈 수 있습니다.
    // 디렉토리 항목은 끊어진 링크를 포함해 실제 마지막 경로와 lstat 속성을 유지합니다.
    ISftpFile? Find(string path)
    {
        path = RemotePaths.Normalize(path);
        if (path == "/")
            return client.Get(path);
        try
        {
            return client.ListDirectory(RemotePaths.Parent(path)!).SingleOrDefault(f => f.Name == RemotePaths.Name(path));
        }
        catch (SftpPathNotFoundException)
        {
            return null;
        }
    }

    public Task<Entry?> Stat(string path, CancellationToken ct) => Task.Run(() => Find(path) is { } file ? Map(file) : null, ct);
    public Task CreateDirectory(string path, CancellationToken ct) => Task.Run(() => client.CreateDirectory(path), ct);
    public Task Delete(string path, bool directory, CancellationToken ct) => Task.Run(() => (Find(path) ?? throw new FileNotFoundException(path)).Delete(), ct);
    public Task Move(string source, string destination, CancellationToken ct) => Task.Run(() =>
    {
        if (Find(destination) != null)
            throw new IOException("대상이 이미 존재합니다.");
        (Find(source) ?? throw new FileNotFoundException(source)).MoveTo(destination);
    }, ct);
    public async Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        using var input = client.OpenRead(remote);
        await using var output = new FileStream(local, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
        input.Position = offset;
        output.Position = offset;
        await StreamCopy.Copy(input, output, offset, input.Length, progress, ct);
    }

    public async Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        await using var input = File.OpenRead(local);
        using var output = client.Open(remote, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write);
        input.Position = offset;
        output.Position = offset;
        await StreamCopy.Copy(input, output, offset, input.Length, progress, ct);
    }

    // SSH.NET은 8진수 권한의 실제 값(420)이 아니라 숫자 표기(644)를 10진수 short로 전달받습니다.
    public Task Chmod(string path, string octal, CancellationToken ct) => Task.Run(() => client.ChangePermissions(path, short.Parse(octal, System.Globalization.CultureInfo.InvariantCulture)), ct);
    public Task SetModified(string path, DateTimeOffset modified, CancellationToken ct) => Task.Run(() => client.SetLastWriteTimeUtc(path, modified.UtcDateTime), ct);
    public Task CreateLink(string target, string link, CancellationToken ct) => Task.Run(() =>
    {
        if (Find(link) != null)
            throw new IOException("링크 경로가 이미 존재합니다.");
        client.SymbolicLink(target, link);
    }, ct);
    public Task SetOwner(string path, int? owner, int? group, CancellationToken ct) => Task.Run(() =>
    {
        var attributes = client.GetAttributes(path);
        if (owner != null)
            attributes.UserId = owner.Value;
        if (group != null)
            attributes.GroupId = group.Value;
        client.SetAttributes(path, attributes);
    }, ct);
    public async Task<string> Command(string command, CancellationToken ct)
    {
        using var ssh = SshConnection.Verify(new SshClient(SshConnection.Info(site, interaction)), site);
        await ssh.ConnectAsync(ct);
        using var cmd = ssh.CreateCommand(command);
        cmd.CommandTimeout = TimeSpan.FromSeconds(30);
        await cmd.ExecuteAsync(ct);
        if (cmd.ExitStatus != 0)
            throw new IOException($"원격 명령 종료 코드 {cmd.ExitStatus}: {cmd.Error}");
        return cmd.Result + cmd.Error;
    }

    public ValueTask DisposeAsync()
    {
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
