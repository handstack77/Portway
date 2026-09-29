using System.Security.Cryptography;
using System.Text;
using Portway.Core;

namespace Portway.Desktop;

public static class DesktopHosting
{
    /// <summary>UI와 백그라운드 처리가 같은 서비스 인스턴스를 공유하도록 등록합니다.</summary>
    public static void AddDesktopServices(this IServiceCollection services)
    {
        services.AddSingleton<AuthenticationBroker>();
        services.AddSingleton<IAuthenticationInteraction>(s => s.GetRequiredService<AuthenticationBroker>());
        services.AddSingleton<RemoteFactory>();
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<Connections>();
        services.AddSingleton<SiteExportService>();
        services.AddSingleton<QueueJournal>();
        services.AddSingleton<TransferQueue>();
        services.AddHostedService(s => s.GetRequiredService<TransferQueue>());
        services.AddSingleton<SyncService>();
        services.AddSingleton<IUpdateClientFactory, VelopackUpdateClientFactory>();
        services.AddSingleton<UpdateService>();
        services.AddHostedService<AutomaticUpdateWorker>();
        services.AddSingleton<LiveSyncService>();
        services.AddHostedService(s => s.GetRequiredService<LiveSyncService>());
        services.AddSingleton<ExternalEditorService>();
        services.AddSingleton<TrashService>();
        services.AddSingleton<TerminalService>();
        services.AddSingleton<DropStore>();
        services.AddHostedService(s => s.GetRequiredService<DropStore>());
        services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));
    }

    /// <summary>loopback・Bearer・Origin・CSPと既存のエラー応答を適用します。</summary>
    public static void UseDesktopSecurity(this WebApplication app, string token)
    {
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; worker-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            if (ctx.Request.Host.Host != "127.0.0.1")
            {
                ctx.Response.StatusCode = 403;
                return;
            }

            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.Headers.CacheControl = "no-store";
                var expected = Encoding.UTF8.GetBytes("Bearer " + token);
                var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString());
                if (!CryptographicOperations.FixedTimeEquals(expected, provided))
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }

                var origin = ctx.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && origin != $"http://{ctx.Request.Host}")
                {
                    ctx.Response.StatusCode = 403;
                    return;
                }
            }

            try
            {
                await next();
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (ctx.Response.HasStarted)
                    throw;
                ctx.Response.StatusCode = ex is KeyNotFoundException or FileNotFoundException ? 404 : ex is InvalidOperationException ? 409 : 400;
                await ctx.Response.WriteAsJsonAsync(new { title = "요청을 완료하지 못했습니다", detail = ex.Message, status = ctx.Response.StatusCode });
            }
        });
    }
}
