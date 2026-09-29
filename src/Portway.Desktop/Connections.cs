using System.Collections.Concurrent;
using Portway.Core;

namespace Portway.Desktop;

public sealed class Connections(RemoteFactory factory, ProfileStore profiles) : IAsyncDisposable
{
    public sealed class Connection(string id, Site site, IRemoteFileSystem fs)
    {
        public string Id { get; } = id;
        public Site Site { get; } = site;
        public IRemoteFileSystem Files { get; } = fs;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    readonly ConcurrentDictionary<string, Connection> items = new();
    public Connection Get(string id) => items.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException("연결이 종료되었습니다.");
    public async Task<object> Connect(Site site, CancellationToken ct)
    {
        site = profiles.Hydrate(site);
        var fs = factory.Create(site);
        try
        {
            await fs.Connect(ct);
            var path = RemotePaths.Normalize(site.RemotePath);
            var listing = await fs.List(path, ct);
            var id = Guid.NewGuid().ToString("N");
            items[id] = new(id, site, fs);
            return new
            {
                id,
                name = site.Name,
                host = site.Host,
                protocol = site.Protocol,
                path,
                capabilities = fs.Capabilities,
                listing = new Listing(path, RemotePaths.Parent(path), listing)
            };
        }
        catch
        {
            await fs.DisposeAsync();
            throw;
        }
    }

    public async Task<T> Use<T>(string id, Func<IRemoteFileSystem, Task<T>> operation, CancellationToken ct)
    {
        var c = Get(id);
        await c.Gate.WaitAsync(ct);
        try
        {
            return await operation(c.Files);
        }
        finally
        {
            c.Gate.Release();
        }
    }

    public async Task Disconnect(string id)
    {
        if (!items.TryRemove(id, out var c))
            return;
        await c.Gate.WaitAsync();
        try
        {
            await c.Files.DisposeAsync();
        }
        finally
        {
            c.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in items.Keys)
            await Disconnect(id);
    }
}
