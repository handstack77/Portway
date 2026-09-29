using System.Collections.Concurrent;
using Portway.Core;

namespace Portway.Desktop;

public record WatchRequest(SyncRequest Sync, int IntervalSeconds = 10, string Name = "지속 동기화");
public record SavedWatch(string Id, WatchRequest Request, Site Site);
public sealed class LiveSyncService : BackgroundService
{
    sealed class Watch(SavedWatch saved)
    {
        public SavedWatch Saved = saved;
        public string Status = "paused", Current = "";
        public string? Error;
        public DateTimeOffset? LastRun;
        public int Changes;
        public CancellationTokenSource Cancel = new();
        public Task? Task;
    }

    readonly Connections connections;
    readonly RemoteFactory factory;
    readonly ProfileStore profiles;
    readonly ConcurrentDictionary<string, Watch> watches = new();
    readonly object persistence = new();
    CancellationToken stopping;
    public LiveSyncService(Connections connections, RemoteFactory factory, ProfileStore profiles)
    {
        this.connections = connections;
        this.factory = factory;
        this.profiles = profiles;
        foreach (var item in profiles.ReadState<SavedWatch[]>("watches.json", []))
            watches[item.Id] = new(item);
    }

    public bool Active => watches.Values.Any(w => w.Task is { IsCompleted: false });

    void Persist()
    {
        lock (persistence)
            profiles.WriteState("watches.json", watches.Values.Select(w => w.Saved with { Site = SiteSecrets.Public(w.Saved.Site) }).ToArray());
    }

    public object[] List() => watches.Values.Select(w =>
    {
        lock (w)
            return (object)new
            {
                w.Saved.Id,
                w.Saved.Request.Name,
                w.Status,
                w.Current,
                w.Error,
                w.LastRun,
                w.Changes,
                localPath = w.Saved.Request.Sync.LocalPath,
                remotePath = w.Saved.Request.Sync.RemotePath,
                site = w.Saved.Site.Name,
                intervalSeconds = w.Saved.Request.IntervalSeconds,
                direction = w.Saved.Request.Sync.Direction
            };
    }).ToArray();
    public string Add(WatchRequest request)
    {
        if (watches.Count >= 20)
            throw new InvalidOperationException("지속 동기화는 최대 20개까지 등록할 수 있습니다.");
        if (request.IntervalSeconds is < 2 or > 86400)
            throw new ArgumentException("실행 간격은 2초–24시간입니다.");
        if (request.Sync.Direction == "both")
            throw new ArgumentException("지속 동기화는 충돌을 피하기 위해 단방향을 선택하세요.");
        if (!Directory.Exists(request.Sync.LocalPath))
            throw new DirectoryNotFoundException(request.Sync.LocalPath);
        var site = connections.Get(request.Sync.SessionId).Site;
        var id = Guid.NewGuid().ToString("N");
        var watch = new Watch(new(id, request, site));
        watches[id] = watch;
        Persist();
        Start(watch);
        return id;
    }

    void Start(Watch watch)
    {
        lock (watch)
        {
            if (watch.Task is { IsCompleted: false })
                throw new InvalidOperationException("기존 동기화가 정지할 때까지 기다리세요.");
            watch.Cancel.Dispose();
            watch.Cancel = new();
            watch.Error = null;
            watch.Status = "watching";
            watch.Task = Task.Run(async () =>
            {
                try
                {
                    await Run(watch);
                }
                catch (Exception e)
                {
                    lock (watch)
                    {
                        watch.Status = "failed";
                        watch.Error = e.Message;
                    }
                }
            });
        }
    }

    public async Task Control(string id, string action, string? sessionId)
    {
        if (!watches.TryGetValue(id, out var watch))
            throw new KeyNotFoundException();
        if (action is "pause" or "delete")
        {
            watch.Cancel.Cancel();
            if (watch.Task != null)
                await watch.Task;
            watch.Status = "paused";
            if (action == "delete")
                watches.TryRemove(id, out _);
            Persist();
        }
        else if (action == "resume")
        {
            var site = sessionId == null ? profiles.Hydrate(watch.Saved.Site) : connections.Get(sessionId).Site;
            if (!SiteSecrets.SameEndpoint(site, watch.Saved.Site) || site.Fingerprint != watch.Saved.Site.Fingerprint)
                throw new InvalidOperationException("같은 서버 연결을 선택하세요.");
            watch.Saved = watch.Saved with
            {
                Site = site
            };
            Start(watch);
        }
        else
            throw new ArgumentException("동기화 작업이 올바르지 않습니다.");
    }

    async Task Run(Watch watch)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, watch.Cancel.Token);
        var ct = linked.Token;
        using var signal = new SemaphoreSlim(0, 1);
        using var watcher = new FileSystemWatcher(watch.Saved.Request.Sync.LocalPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        void Changed(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (!e.Name!.Contains(".portway-", StringComparison.Ordinal) && signal.CurrentCount == 0)
                    signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        watcher.Changed += Changed;
        watcher.Created += Changed;
        watcher.Deleted += Changed;
        watcher.Renamed += Changed;
        watcher.EnableRaisingEvents = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    lock (watch)
                    {
                        watch.Status = "running";
                        watch.Error = null;
                    }

                    await using var remote = factory.Create(watch.Saved.Site);
                    await remote.Connect(ct);
                    var plan = await Synchronizer.Preview(remote, watch.Saved.Request.Sync, ct);
                    if (plan.Changes.Length > 0)
                        await Synchronizer.Apply(remote, watch.Saved.Request.Sync, plan, path =>
                        {
                            lock (watch)
                                watch.Current = path;
                        }, ct);
                    lock (watch)
                    {
                        watch.Status = "watching";
                        watch.LastRun = DateTimeOffset.UtcNow;
                        watch.Changes += plan.Changes.Length;
                        watch.Current = "변경 감시 중";
                    }
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    lock (watch)
                    {
                        watch.Status = "retrying";
                        watch.Error = e.Message;
                    }
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(watch.Saved.Request.IntervalSeconds), ct);
                await Task.Delay(500, ct); // 원자적 저장과 연속된 파일 시스템 이벤트를 묶어 처리합니다.
                while (signal.CurrentCount > 0)
                    await signal.WaitAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            lock (watch)
                watch.Status = "paused";
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stopping = stoppingToken;
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var watch in watches.Values)
            watch.Cancel.Cancel();
        await Task.WhenAll(watches.Values.Where(w => w.Task != null).Select(w => w.Task!));
    }
}
