using System.Security.Cryptography;
using System.Text;

namespace Portway.Server;
// 공개 다운로드와 인증된 업로드의 기존 경계를 유지합니다.
public static class ServerApi
{
    public static void MapDistributionApi(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/releases", (ReleaseStore store) => store.List());
        app.MapGet("/releases/{channel}/{file}", (string channel, string file, HttpContext ctx, ReleaseStore store) =>
        {
            var path = store.Resolve(channel, file);
            if (path == null)
                return Results.NotFound();
            ctx.Response.Headers.CacheControl = file.StartsWith("releases.") || file.StartsWith("RELEASES") ? "no-cache" : "public,max-age=3600";
            return Results.File(path, file.EndsWith(".json") ? "application/json" : "application/octet-stream", enableRangeProcessing: true);
        });
        app.MapPost("/api/releases/{channel}", async (string channel, HttpContext ctx, ReleaseStore store, IConfiguration config) =>
        {
            var key = config["Distribution:ApiKey"];
            if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
                return Results.Problem("배포 API 키가 설정되지 않았습니다.", statusCode: 503);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes("Bearer " + key), Encoding.UTF8.GetBytes(ctx.Request.Headers.Authorization.ToString())))
                return Results.Unauthorized();
            if (ctx.Request.ContentType != "application/zip")
                return Results.Problem("application/zip 요청이 필요합니다.", statusCode: 415);
            return Results.Ok(await store.Publish(channel, ctx.Request.Body, ctx.RequestAborted));
        }).RequireRateLimiting("publish");
    }
}
