using System.Collections.Concurrent;
using Portway.Core;

namespace Portway.Desktop;

public sealed class SyncService(Connections connections, RemoteFactory factory, IHostApplicationLifetime lifetime)
{
    sealed class State(SyncRequest request, Site site, SyncPlan plan)
    {
        public SyncRequest Request = request;
        public Site Site = site;
        public SyncPlan Plan = plan;
        public DateTimeOffset Created = DateTimeOffset.UtcNow;
        public string Status = "preview";
        public string Current = "";
        public string? Error;
        public CancellationTokenSource Cancel = new();
    }

    readonly ConcurrentDictionary<string, State> plans = new();
    public bool Active => plans.Values.Any(p => p.Status == "running");

    public async Task<object> Preview(SyncRequest request, CancellationToken ct)
    {
        foreach (var old in plans.Where(p => p.Value.Created < DateTimeOffset.UtcNow.AddMinutes(-30) && p.Value.Status != "running"))
            plans.TryRemove(old.Key, out _);
        if (plans.Count > 50)
            throw new InvalidOperationException("동기화 미리보기 한도를 초과했습니다.");
        var site = connections.Get(request.SessionId).Site;
        await using var remote = factory.Create(site);
        await remote.Connect(ct);
        var plan = await Synchronizer.Preview(remote, request, ct);
        var id = Guid.NewGuid().ToString("N");
        plans[id] = new(request, site, plan);
        return new
        {
            id,
            changes = plan.Changes
        };
    }

    public object Status(string id)
    {
        var p = Get(id);
        lock (p)
            return new
            {
                id,
                p.Status,
                p.Current,
                p.Error,
                count = p.Plan.Changes.Length
            };
    }

    State Get(string id) => plans.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException("미리보기가 만료되었습니다.");
    public object Apply(string id, string[]? selected = null, IReadOnlyDictionary<string, string>? resolutions = null)
    {
        var p = Get(id);
        lock (p)
        {
            if (p.Status != "preview" || p.Created < DateTimeOffset.UtcNow.AddMinutes(-30))
                throw new InvalidOperationException("새 미리보기가 필요합니다.");
            p.Status = "running";
        }

        _ = Task.Run(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping, p.Cancel.Token);
            try
            {
                await using var remote = factory.Create(p.Site);
                await remote.Connect(linked.Token);
                await Synchronizer.Apply(remote, p.Request, p.Plan, file =>
                {
                    lock (p)
                        p.Current = file;
                }, linked.Token, selected, resolutions);
                lock (p)
                    p.Status = "completed";
            }
            catch (Exception e)
            {
                lock (p)
                {
                    p.Status = linked.IsCancellationRequested ? "cancelled" : "failed";
                    p.Error = e.Message;
                }
            }
        });
        return Status(id);
    }

    public void Cancel(string id) => Get(id).Cancel.Cancel();
}
