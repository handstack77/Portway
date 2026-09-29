using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Portway.Core;
using Portway.Desktop;
using Velopack;

namespace Portway.Tests;

public sealed class UpdateTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-updates-" + Guid.NewGuid().ToString("N"));
    readonly ProfileStore profiles;
    readonly FakeClient client = new();
    readonly FakeFactory factory;
    public UpdateTests()
    {
        profiles = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = root }).Build());
        profiles.Preferences(new Preferences(UpdateUrl: "https://updates.example.test/releases/win-x64-stable/"));
        factory = new(client);
    }

    UpdateService Service() => new(profiles, factory, NullLogger<UpdateService>.Instance);
    [Fact]
    public async Task StartupPreparesUpdateButNeverAppliesDuringTheRunningSession()
    {
        var service = Service();
        await service.PrepareAutomaticUpdate(CancellationToken.None);
        Assert.Equal("ready", service.Status.State);
        Assert.True(service.Status.Ready);
        Assert.Equal("0.3.3", service.Status.Version);
        Assert.Equal(100, service.Status.Progress);
        Assert.Equal(1, client.Checks);
        Assert.Equal(1, client.Downloads);
        Assert.Equal(0, client.Applies);
        // 설정 화면을 열거나 수동으로 확인해도 준비 상태를 잃거나
        // 백그라운드 작업이 준비한 파일을 다시 다운로드하지 않아야 합니다.
        Assert.True((await service.Check()).Ready);
        Assert.True((await service.Download()).Ready);
        Assert.Equal(1, client.Checks);
        Assert.Equal(1, client.Downloads);
        await service.Apply();
        Assert.Equal(1, client.Applies);
    }

    [Fact]
    public async Task PendingPackageIsAvailableWithoutNetworkInANewServiceInstance()
    {
        await Service().PrepareAutomaticUpdate(CancellationToken.None);
        client.OnCheck = _ => throw new IOException("offline");
        var reopened = Service();
        Assert.True((await reopened.Check()).Ready);
        Assert.Equal(1, client.Checks);
        Assert.Equal(1, client.Downloads);
        await reopened.Apply();
        Assert.Equal(1, client.Applies);
    }

    [Theory]
    [InlineData("no-feed", "disabled", 0)]
    [InlineData("source-build", "unavailable", 0)]
    [InlineData("up-to-date", "current", 1)]
    public async Task StartupSkipsUnavailableUpdates(string scenario, string state, int checks)
    {
        if (scenario == "no-feed")
            profiles.Preferences(new());
        if (scenario == "source-build")
            client.IsInstalled = false;
        if (scenario == "up-to-date")
            client.OnCheck = _ => Task.FromResult<UpdateInfo?>(null);
        var service = Service();
        await service.PrepareAutomaticUpdate(CancellationToken.None);
        Assert.Equal(state, service.Status.State);
        Assert.Equal(checks, client.Checks);
        Assert.Equal(0, client.Downloads);
        Assert.Equal(0, client.Applies);
        if (scenario == "no-feed")
            Assert.Empty(factory.Urls);
    }

    [Fact]
    public async Task NetworkFailureDoesNotEscapeStartupAndNextLaunchCanRetry()
    {
        client.OnCheck = _ => throw new IOException("offline with sensitive URL");
        var service = Service();
        await service.PrepareAutomaticUpdate(CancellationToken.None);
        Assert.Equal("error", service.Status.State);
        Assert.DoesNotContain("sensitive", service.Status.Message);
        Assert.False(service.Status.Ready);
        client.OnCheck = null;
        var next = Service();
        await next.PrepareAutomaticUpdate(CancellationToken.None);
        Assert.True(next.Status.Ready);
        Assert.Equal(0, client.Applies);
    }

    [Fact]
    public async Task FailedDownloadCannotBeAppliedAndManualRetryWorks()
    {
        client.OnDownload = (_, _) => throw new IOException("checksum mismatch");
        var service = Service();
        await service.PrepareAutomaticUpdate(CancellationToken.None);
        Assert.Equal("error", service.Status.State);
        Assert.False(service.Status.Ready);
        Assert.Null(client.PendingRestart);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Apply());
        client.OnDownload = null;
        Assert.True((await service.Download()).Ready);
        Assert.Equal(2, client.Downloads);
        Assert.Equal(0, client.Applies);
    }

    [Fact]
    public async Task AutomaticAndManualRequestsShareASingleDownload()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnDownload = async (progress, ct) =>
        {
            progress(50);
            entered.SetResult();
            await finish.Task.WaitAsync(ct);
        };
        var service = Service();
        var automatic = service.PrepareAutomaticUpdate(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("downloading", service.Status.State);
        Assert.Equal(50, service.Status.Progress);
        var manualCheck = service.Check();
        var manualDownload = service.Download();
        Assert.False(manualCheck.IsCompleted);
        Assert.False(manualDownload.IsCompleted);
        finish.SetResult();
        await Task.WhenAll(automatic, manualCheck, manualDownload).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.Status.Ready);
        Assert.Equal(1, client.Checks);
        Assert.Equal(1, client.Downloads);
        Assert.Equal(0, client.Applies);
    }

    [Fact]
    public async Task ChangingTheFeedRequiresAnotherCheckBeforeDownloading()
    {
        var service = Service();
        await service.Check();
        profiles.Preferences(profiles.Preferences() with { UpdateUrl = "https://other.example.test/releases/" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Download());
        Assert.Equal(0, client.Downloads);
        await service.Check();
        await service.Download();
        Assert.Equal(2, factory.Urls.Count);
        Assert.Equal(1, client.Downloads);
    }

    [Fact]
    public async Task InvalidPublicHttpFeedIsNeverContacted()
    {
        profiles.Preferences(new Preferences(UpdateUrl: "http://updates.example.test/"));
        var service = Service();
        await service.PrepareAutomaticUpdate(CancellationToken.None);
        Assert.Equal("error", service.Status.State);
        Assert.Empty(factory.Urls);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Check());
    }

    [Fact]
    public async Task HostStartsWithoutWaitingForNetworkAndCancelsDownloadOnShutdown()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnDownload = async (_, ct) =>
        {
            entered.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                cancelled.SetResult();
            }
        };
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(profiles);
            services.AddSingleton<IUpdateClientFactory>(factory);
            services.AddSingleton<UpdateService>();
            services.AddHostedService<AutomaticUpdateWorker>();
        }).Build();
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var service = host.Services.GetRequiredService<UpdateService>();
        Assert.Equal("cancelled", service.Status.State);
        Assert.False(service.Status.Ready);
        Assert.Null(client.PendingRestart);
        Assert.Equal(0, client.Applies);
    }

    sealed class FakeFactory(FakeClient client) : IUpdateClientFactory
    {
        public List<string> Urls { get; } = [];

        public IUpdateClient Create(string url)
        {
            Urls.Add(url);
            return client;
        }
    }

    sealed class FakeClient : IUpdateClient
    {
        readonly VelopackAsset release = new()
        {
            PackageId = "Portway",
            Version = SemanticVersion.Parse("0.3.3"),
            Type = VelopackAssetType.Full,
            FileName = "Portway-0.3.3-full.nupkg"
        };
        public bool IsInstalled { get; set; } = true;
        public VelopackAsset? PendingRestart { get; private set; }
        public Func<CancellationToken, Task<UpdateInfo?>>? OnCheck { get; set; }
        public Func<Action<int>, CancellationToken, Task>? OnDownload { get; set; }

        public int Checks, Downloads, Applies;
        public Task<UpdateInfo?> Check(CancellationToken ct)
        {
            Interlocked.Increment(ref Checks);
            return OnCheck?.Invoke(ct) ?? Task.FromResult<UpdateInfo?>(new UpdateInfo(release, false));
        }

        public async Task Download(UpdateInfo update, Action<int> progress, CancellationToken ct)
        {
            Interlocked.Increment(ref Downloads);
            if (OnDownload != null)
                await OnDownload(progress, ct);
            ct.ThrowIfCancellationRequested();
            PendingRestart = update.TargetFullRelease;
            progress(100);
        }

        public void Apply(VelopackAsset asset)
        {
            Assert.Same(PendingRestart, asset);
            Interlocked.Increment(ref Applies);
        }
    }

    public void Dispose()
    {
        profiles.Dispose();
        Directory.Delete(root, true);
    }
}
