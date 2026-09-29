using Velopack;
using Velopack.Sources;

namespace Portway.Desktop;

public sealed record UpdateStatus(string State = "idle", bool Installed = false, bool Available = false, bool Ready = false, string? Version = null, int Progress = 0, string? Message = null);
// 실행 중인 테스트 프로세스를 교체하지 않고 시작 동작과 동시 실행을 검증합니다.
public interface IUpdateClient
{
    bool IsInstalled { get; }

    VelopackAsset? PendingRestart { get; }

    Task<UpdateInfo?> Check(CancellationToken ct);
    Task Download(UpdateInfo update, Action<int> progress, CancellationToken ct);
    void Apply(VelopackAsset release);
}

public interface IUpdateClientFactory
{
    IUpdateClient Create(string url);
}

public sealed class VelopackUpdateClientFactory : IUpdateClientFactory
{
    public IUpdateClient Create(string url) => new Client(url);
    sealed class Client(string url) : IUpdateClient
    {
        readonly UpdateManager manager = new(new SimpleWebSource(url, null, 30));
        public bool IsInstalled => manager.IsInstalled;
        public VelopackAsset? PendingRestart => manager.UpdatePendingRestart;

        // 업데이트 피드 조회에는 취소 매개변수가 없으므로 네트워크 제한 시간을 설정하고
        // 종료 시 대기를 중단합니다. 이 조회에서는 패키지를 다운로드하거나 적용하지 않습니다.
        public Task<UpdateInfo?> Check(CancellationToken ct) => manager.CheckForUpdatesAsync().WaitAsync(ct);
        public Task Download(UpdateInfo update, Action<int> progress, CancellationToken ct) => manager.DownloadUpdatesAsync(update, progress, ct);
        public void Apply(VelopackAsset release) => manager.ApplyUpdatesAndRestart(release);
    }
}

public sealed class UpdateService(ProfileStore profiles, IUpdateClientFactory clients, ILogger<UpdateService> log)
{
    readonly SemaphoreSlim gate = new(1, 1);
    IUpdateClient? client;
    string? clientUrl;
    UpdateInfo? update;
    VelopackAsset? prepared;
    UpdateStatus status = new();
    public UpdateStatus Status => Volatile.Read(ref status);

    void SetStatus(UpdateStatus value) => Volatile.Write(ref status, value);
    public static void ValidateUrl(string url)
    {
        if (url.Length == 0)
            return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) || uri.UserInfo.Length > 0)
            throw new ArgumentException("업데이트 주소는 HTTPS URL이어야 합니다. 로컬 테스트는 HTTP를 사용할 수 있습니다.");
    }

    public Task<UpdateStatus> Check(CancellationToken ct = default) => Exclusive(() => CheckCore(ct), ct);
    public Task<UpdateStatus> Download(CancellationToken ct = default) => Exclusive(() => DownloadCore(ct), ct);
    public async Task PrepareAutomaticUpdate(CancellationToken ct)
    {
        try
        {
            await Exclusive(async () =>
            {
                if (string.IsNullOrWhiteSpace(profiles.Preferences().UpdateUrl))
                {
                    SetStatus(new("disabled", Message: "피드 URL을 저장하면 다음 실행부터 자동으로 업데이트를 확인합니다."));
                    return Status;
                }

                var result = await CheckCore(ct);
                return result.Available && !result.Ready ? await DownloadCore(ct) : result;
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // URL에는 접근 토큰이 포함될 수 있으므로 예외 메시지나 URL을 로그에 기록하지 않습니다.
            log.LogWarning("시작 시 업데이트 준비에 실패했습니다({ErrorType}). 앱은 계속 실행되며 다음 실행 때 다시 시도합니다.", ex.GetType().Name);
        }
    }

    async Task<UpdateStatus> CheckCore(CancellationToken ct)
    {
        var url = profiles.Preferences().UpdateUrl.Trim();
        ValidateUrl(url);
        if (url.Length == 0)
            throw new InvalidOperationException("설정에서 배포 서버의 피드 주소를 입력하세요.");
        if (client == null || clientUrl != url)
        {
            client = clients.Create(url);
            clientUrl = url;
            update = null;
            prepared = null;
        }

        if (!client.IsInstalled)
        {
            SetStatus(new("unavailable", Message: "vpk 설치판 또는 AppImage에서 자동 업데이트를 사용할 수 있습니다."));
            return Status;
        }

        // 수동 확인에서도 이미 준비한 업데이트를 유지합니다. 패키지 캐시는
        // 프로세스를 다시 시작해도 보존되며 다음 실행 때 VelopackApp이 적용합니다.
        var pending = client.PendingRestart;
        if (pending != null)
        {
            SetReady(pending);
            return Status;
        }

        prepared = null;
        update = null;
        SetStatus(new("checking", Installed: true, Message: "업데이트 확인 중…"));
        update = await client.Check(ct);
        ct.ThrowIfCancellationRequested();
        SetStatus(update == null ? new("current", Installed: true, Message: "최신 버전입니다.") : new("available", Installed: true, Available: true, Version: update.TargetFullRelease.Version.ToString()));
        return Status;
    }

    async Task<UpdateStatus> DownloadCore(CancellationToken ct)
    {
        if (Status.Ready)
            return Status;
        if (client == null || update == null)
            throw new InvalidOperationException("먼저 업데이트를 확인하세요.");
        if (clientUrl != profiles.Preferences().UpdateUrl.Trim())
            throw new InvalidOperationException("피드 주소가 변경되었습니다. 업데이트를 다시 확인하세요.");
        SetStatus(new("downloading", Installed: true, Available: true, Version: update.TargetFullRelease.Version.ToString(), Message: "업데이트 다운로드 중…"));
        await client.Download(update, percent =>
        {
            var current = Status;
            if (current.State == "downloading")
                SetStatus(current with { Progress = Math.Clamp(percent, 0, 100) });
        }, ct);
        // Velopack은 다운로드와 검증이 완료된 패키지만 시작 시 적용할 캐시에 등록합니다.
        SetReady(update.TargetFullRelease);
        return Status;
    }

    void SetReady(VelopackAsset release)
    {
        prepared = release;
        SetStatus(new("ready", Installed: true, Available: true, Ready: true, Version: release.Version.ToString(), Progress: 100, Message: $"새 버전 {release.Version} 다운로드 완료. 앱을 종료한 뒤 다음 실행 시 자동 적용됩니다."));
    }

    public async Task Apply(CancellationToken ct = default)
    {
        await Exclusive(() =>
        {
            if (!Status.Ready || prepared == null || client == null)
                throw new InvalidOperationException("업데이트를 먼저 다운로드하세요.");
            client.Apply(prepared);
            return Task.FromResult(Status);
        }, ct);
    }

    async Task<UpdateStatus> Exclusive(Func<Task<UpdateStatus>> operation, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!Status.Ready)
                SetStatus(Status with { State = "cancelled", Progress = 0, Message = "업데이트 준비가 중단되었습니다. 다음 실행 또는 수동 확인으로 다시 시도합니다." });
            throw;
        }
        catch
        {
            if (!Status.Ready)
                SetStatus(Status with { State = "error", Progress = 0, Message = "업데이트를 준비하지 못했습니다. 앱은 계속 사용할 수 있으며 다음 실행 또는 수동 확인으로 다시 시도합니다." });
            throw;
        }
        finally
        {
            gate.Release();
        }
    }
}
