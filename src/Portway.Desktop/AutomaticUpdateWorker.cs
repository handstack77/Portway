namespace Portway.Desktop;

public sealed class AutomaticUpdateWorker(UpdateService updates, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // STA/UI 시작 경로나 설치 훅에서는 네트워크 작업을 수행하지 않습니다.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(stoppingToken);
            await updates.PrepareAutomaticUpdate(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
