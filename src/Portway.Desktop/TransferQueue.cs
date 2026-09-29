using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Portway.Core;

namespace Portway.Desktop;

/// <summary>전송 작업을 영속화하고 worker의 예약·재시도·일시정지와 복구를 관리합니다.</summary>
public sealed class TransferQueue : BackgroundService
{
    public sealed class Job
    {
        public required string Id;
        public required TransferRequest Request;
        public required Site Site;
        public string Status = "queued";
        public string? Error;
        public string Current = "";
        public long Bytes, Total;
        public double Speed;
        public DateTimeOffset Created = DateTimeOffset.UtcNow;
        public DateTimeOffset? RetryAt;
        public int Attempts;
        public bool Restored;
        public CancellationTokenSource Cancel = new();
        public bool Pause;
        public Dictionary<string, Entry> Sources = new();
        public Dictionary<string, Entry> FileSnapshots = new();
        public HashSet<string> Completed = [];
        public HashSet<string> CompletedFiles = [];
        public TransferCheckpoint? Checkpoint;
        public long LastCheckpoint;
    }

    readonly Connections connections;
    readonly RemoteFactory factory;
    readonly ProfileStore profiles;
    readonly QueueJournal journal;
    readonly ConcurrentDictionary<string, Job> jobs = new();
    readonly SemaphoreSlim signal = new(0);
    readonly object dispatch = new();
    public TransferQueue(Connections connections, RemoteFactory factory, ProfileStore profiles, QueueJournal journal)
    {
        this.connections = connections;
        this.factory = factory;
        this.profiles = profiles;
        this.journal = journal;
        foreach (var item in journal.Load())
        {
            var terminal = item.Status is "completed" or "cancelled" or "failed";
            jobs[item.Id] = new Job
            {
                Id = item.Id,
                Request = item.Request,
                Site = item.Site,
                Created = item.Created,
                Status = terminal ? item.Status : "paused",
                Error = terminal ? item.Error : item.Request.Direction == "local" ? "앱이 다시 시작되었습니다. 로컬 전송 이어하기를 선택하세요." : "앱이 다시 시작되었습니다. 연결 후 이어하기를 선택하세요.",
                Sources = item.Sources,
                FileSnapshots = item.FileSnapshots,
                Completed = item.Completed,
                CompletedFiles = item.CompletedFiles,
                Attempts = item.Attempts,
                Bytes = item.Bytes,
                Total = item.Total,
                Current = item.Current,
                Checkpoint = item,
                Restored = true
            };
        }
    }

    public bool Active => jobs.Values.Any(j => j.Status is "queued" or "running" or "pausing" or "retrying");

    public (string Id, string Status)? FindSourceJob(string directory)
    {
        var prefix = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        (string Id, string Status)? completed = null;
        foreach (var job in jobs.Values)
            lock (job)
                if (job.Request.Direction == "upload" && job.Request.Paths.Any(Within))
                {
                    if (job.Status != "completed")
                        return (job.Id, job.Status);
                    completed = (job.Id, job.Status);
                }

        return completed;
        bool Within(string path)
        {
            try
            {
                return Path.GetFullPath(path).StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
            {
                return false;
            }
        }
    }

    public object[] List() => jobs.Values.OrderByDescending(j => j.Created).Select(j =>
    {
        lock (j)
            return (object)new
            {
                j.Id,
                j.Status,
                j.Error,
                j.Current,
                j.Bytes,
                j.Total,
                j.Speed,
                j.Created,
                j.Restored,
                j.Attempts,
                j.RetryAt,
                direction = j.Request.Direction,
                paths = j.Request.Paths,
                destination = j.Request.Destination,
                site = j.Site.Name,
                siteId = j.Site.Id,
                priority = j.Request.Priority,
                scheduledAt = j.Request.ScheduledAt
            };
    }).ToArray();
    void Persist(Job job, bool checkpoint = false)
    {
        lock (job)
        {
            if (checkpoint || job.Checkpoint == null)
                job.Checkpoint = new(job.Id, job.Request, SiteSecrets.Public(job.Site), job.Status, job.Error, job.Created, new(job.Sources), new(job.FileSnapshots), new(job.Completed), new(job.CompletedFiles));
            job.Checkpoint = job.Checkpoint with
            {
                Request = job.Request,
                Status = job.Status,
                Error = job.Error,
                Attempts = job.Attempts,
                Bytes = job.Bytes,
                Total = job.Total,
                Current = job.Current
            };
            journal.Save(job.Checkpoint);
            job.LastCheckpoint = Environment.TickCount64;
        }
    }

    public string Add(TransferRequest request)
    {
        if (request.Direction is not ("upload" or "download" or "local") || request.Conflict is not ("skip" or "replace" or "newer" or "rename"))
            throw new ArgumentException("전송 옵션이 올바르지 않습니다.");
        if (request.Paths.Length is 0 or > 10000 || string.IsNullOrWhiteSpace(request.Destination))
            throw new ArgumentException("전송할 파일과 대상 폴더가 필요합니다.");
        if (jobs.Count >= 1000)
            throw new InvalidOperationException("완료된 전송 기록을 정리하세요.");
        var options = request.Options ?? profiles.Preferences().TransferDefaults ?? new();
        options.Validate();
        request = request with
        {
            Options = options,
            Priority = Math.Clamp(request.Priority, -10, 10)
        };
        if (request.Direction == "local")
            request = LocalTransferFileSystem.Validate(request);
        var job = new Job
        {
            Id = Guid.NewGuid().ToString("N"),
            Request = request,
            Site = request.Direction == "local" ? new Site
            {
                Id = "local",
                Name = "내 컴퓨터",
                Host = "local"
            }

            : connections.Get(request.SessionId).Site,
            Status = request.ScheduledAt > DateTimeOffset.UtcNow ? "scheduled" : "queued"
        };
        Persist(job, true);
        jobs[job.Id] = job;
        signal.Release();
        return job.Id;
    }

    public void Control(string id, string action, string? sessionId = null)
    {
        if (!jobs.TryGetValue(id, out var job))
            throw new KeyNotFoundException();
        lock (job)
        {
            if (action is "pause" or "cancel")
            {
                if (job.Status is "completed" or "cancelled")
                    return;
                job.Pause = action == "pause";
                if (job.Status is "running" or "pausing")
                {
                    job.Status = "pausing";
                    job.Cancel.Cancel();
                }
                else
                    job.Status = job.Pause ? "paused" : "cancelled";
            }
            else if (action == "retry" && job.Status is "failed" or "paused" or "cancelled")
            {
                if (job.Request.Direction != "local" && sessionId != null)
                {
                    var site = connections.Get(sessionId).Site;
                    if (!SiteSecrets.SameEndpoint(job.Site, site) || job.Site.Fingerprint != site.Fingerprint)
                        throw new InvalidOperationException("원래 서버와 호스트 키가 같은 연결을 선택하세요.");
                    job.Site = site;
                }
                else if (job.Request.Direction != "local" && job.Restored)
                    job.Site = profiles.Hydrate(job.Site);
                job.Cancel.Dispose();
                job.Cancel = new();
                job.Pause = false;
                job.Error = null;
                job.Attempts = 0;
                job.RetryAt = null;
                job.Status = "queued";
                job.Restored = false;
            }
            else if (action is "priority-up" or "priority-down")
                job.Request = job.Request with
                {
                    Priority = Math.Clamp(job.Request.Priority + (action == "priority-up" ? 1 : -1), -10, 10)
                };
            Persist(job);
            signal.Release();
        }
    }

    public void Clear()
    {
        foreach (var job in jobs.Values.Where(j => j.Status is "completed" or "failed" or "cancelled"))
            lock (job)
                if (job.Status is "completed" or "failed" or "cancelled" && jobs.TryRemove(job.Id, out _))
                {
                    journal.Delete(job.Id);
                    job.Cancel.Dispose();
                }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(Enumerable.Range(0, 8).Select(index => Worker(index, stoppingToken)));
    async Task Worker(int index, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            Job? job = null;
            lock (dispatch)
            {
                if (index < Math.Clamp(profiles.Preferences().MaxConcurrent, 1, 8))
                    foreach (var candidate in jobs.Values.OrderByDescending(j => j.Request.Priority).ThenBy(j => j.Created))
                    {
                        lock (candidate)
                        {
                            if (candidate.Status is not ("queued" or "scheduled" or "retrying") || candidate.Request.ScheduledAt > DateTimeOffset.UtcNow || candidate.RetryAt > DateTimeOffset.UtcNow)
                                continue;
                            candidate.Status = "running";
                            job = candidate;
                            break;
                        }
                    }
            }

            if (job == null)
            {
                try
                {
                    await signal.WaitAsync(TimeSpan.FromSeconds(1), stopping);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, job.Cancel.Token);
            var ct = linked.Token;
            try
            {
                Persist(job, true);
                var local = job.Request.Direction == "local";
                if (local)
                    _ = LocalTransferFileSystem.Validate(job.Request with { Paths = job.Request.Paths.Where(path => !job.Completed.Contains(path) && !job.CompletedFiles.Contains(path)).ToArray() });
                await using IRemoteFileSystem remote = local ? new LocalTransferFileSystem() : factory.Create(job.Site);
                await remote.Connect(ct);
                var watch = Stopwatch.StartNew();
                long initialBytes = 0;
                string lastFile = "";
                foreach (var path in job.Request.Paths)
                {
                    if (job.Completed.Contains(path))
                        continue;
                    if (local && job.CompletedFiles.Contains(path))
                    {
                        job.Completed.Add(path);
                        Persist(job, true);
                        continue;
                    }

                    var info = job.Request.Direction == "upload" ? LocalFiles.Stat(path) : await remote.Stat(path, ct) ?? throw new FileNotFoundException(path);
                    if (job.Sources.TryGetValue(path, out var previous) && (previous.Size != info.Size || !info.IsDirectory && previous.Modified != info.Modified))
                        throw new IOException("원본이 변경되어 이어받을 수 없습니다. 새 전송을 시작하세요.");
                    job.Sources[path] = info;
                    Persist(job, true);
                    await TransferOperations.Transfer(remote, local ? "download" : job.Request.Direction, path, job.Request.Destination, job.Request.Conflict, job.Id, (file, progress) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var changedFile = lastFile != file;
                        if (changedFile)
                        {
                            lastFile = file;
                            initialBytes = progress.Bytes;
                            watch.Restart();
                        }

                        var elapsed = Math.Max(watch.Elapsed.TotalSeconds, .001);
                        lock (job)
                        {
                            job.Current = file;
                            job.Bytes = progress.Bytes;
                            job.Total = progress.Total;
                            job.Speed = Math.Max(0, (progress.Bytes - initialBytes) / elapsed);
                        }

                        if (changedFile || Environment.TickCount64 - job.LastCheckpoint > 500)
                            Persist(job, true);
                        if (job.Request.SpeedLimit > 0)
                        {
                            var delay = (progress.Bytes - initialBytes) / (job.Request.SpeedLimit * 1024.0) - watch.Elapsed.TotalSeconds;
                            while (delay > 0)
                            {
                                if (ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Min(delay, 1))))
                                    ct.ThrowIfCancellationRequested();
                                delay = (progress.Bytes - initialBytes) / (job.Request.SpeedLimit * 1024.0) - watch.Elapsed.TotalSeconds;
                            }
                        }
                    }, ct, snapshots: job.FileSnapshots, completed: job.CompletedFiles, options: job.Request.Options);
                    job.Completed.Add(path);
                    Persist(job, true);
                }

                lock (job)
                {
                    job.Status = "completed";
                    job.Speed = 0;
                    job.Error = null;
                }
            }
            catch (Exception error)
            {
                lock (job)
                {
                    job.Speed = 0;
                    if (ct.IsCancellationRequested)
                    {
                        job.Status = stopping.IsCancellationRequested || job.Pause ? "paused" : "cancelled";
                        job.Error = null;
                    }
                    else if (IsTransient(error) && job.Attempts < (job.Request.Options?.MaxRetries ?? 2))
                    {
                        job.Attempts++;
                        job.Status = "retrying";
                        job.RetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, job.Attempts));
                        job.Error = error.Message;
                    }
                    else
                    {
                        job.Status = "failed";
                        job.Error = error.Message;
                    }
                }
            }
            finally
            {
                Persist(job, true);
                signal.Release();
            }
        }
    }

    static bool IsTransient(Exception error) => error is SocketException or HttpRequestException or Renci.SshNet.Common.SshConnectionException || error is FluentFTP.Exceptions.FtpException and not FluentFTP.Exceptions.FtpCommandException || error is Amazon.Runtime.AmazonServiceException aws && (int)aws.StatusCode >= 500;
}
