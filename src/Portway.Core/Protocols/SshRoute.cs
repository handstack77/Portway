using Renci.SshNet;

namespace Portway.Core.Protocols;

public sealed class SshRoute : IAsyncDisposable
{
    SshClient? jump;
    ForwardedPortLocal? forwarding;
    public Site Target { get; private set; } = new();

    public static async Task<SshRoute> Open(Site site, IAuthenticationInteraction? interaction, CancellationToken ct)
    {
        var route = new SshRoute
        {
            Target = site
        };
        if (site.Jump == null)
            return route;
        try
        {
            route.jump = SshConnection.Verify(new SshClient(SshConnection.Info(site.Jump, interaction)), site.Jump);
            await route.jump.ConnectAsync(ct);
            route.forwarding = new ForwardedPortLocal("127.0.0.1", 0, site.Host, (uint)site.EffectivePort);
            route.jump.AddForwardedPort(route.forwarding);
            route.forwarding.Start();
            route.Target = site with
            {
                Host = "127.0.0.1",
                Port = (int)route.forwarding.BoundPort,
                Proxy = new(),
                Jump = null
            };
            return route;
        }
        catch
        {
            await route.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        forwarding?.Dispose();
        jump?.Dispose();
        return ValueTask.CompletedTask;
    }
}

sealed class TunneledFileSystem(Site site, IAuthenticationInteraction? interaction, Func<Site, IRemoteFileSystem> create) : IRemoteFileSystem
{
    SshRoute? route;
    IRemoteFileSystem? inner;
    IRemoteFileSystem Files => inner ?? throw new InvalidOperationException("먼저 연결하세요.");
    public Capabilities Capabilities => Files.Capabilities;

    public async Task Connect(CancellationToken ct)
    {
        route = await SshRoute.Open(site, interaction, ct);
        inner = create(route.Target);
        await inner.Connect(ct);
    }

    public Task<Entry[]> List(string path, CancellationToken ct) => Files.List(path, ct);
    public Task<Entry?> Stat(string path, CancellationToken ct) => Files.Stat(path, ct);
    public Task CreateDirectory(string path, CancellationToken ct) => Files.CreateDirectory(path, ct);
    public Task Delete(string path, bool directory, CancellationToken ct) => Files.Delete(path, directory, ct);
    public Task Move(string source, string destination, CancellationToken ct) => Files.Move(source, destination, ct);
    public Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct) => Files.Download(remote, local, offset, progress, ct);
    public Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct) => Files.Upload(local, remote, offset, progress, ct);
    public Task Chmod(string path, string octal, CancellationToken ct) => Files.Chmod(path, octal, ct);
    public Task SetModified(string path, DateTimeOffset modified, CancellationToken ct) => Files.SetModified(path, modified, ct);
    public Task CreateLink(string target, string link, CancellationToken ct) => Files.CreateLink(target, link, ct);
    public Task SetOwner(string path, int? owner, int? group, CancellationToken ct) => Files.SetOwner(path, owner, group, ct);
    public Task Copy(string source, string destination, CancellationToken ct) => Files.Copy(source, destination, ct);
    public Task<string> Command(string command, CancellationToken ct) => Files.Command(command, ct);
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (inner != null)
                await inner.DisposeAsync();
        }
        finally
        {
            if (route != null)
                await route.DisposeAsync();
        }
    }
}
