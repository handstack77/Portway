using Microsoft.AspNetCore.RateLimiting;

using Portway.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ReleaseStore>();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("publish", opt =>
    {
        opt.PermitLimit = 12;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024);
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'";
    try
    {
        await next();
    }
    catch (Exception e) when (e is IOException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
    {
        if (ctx.Response.HasStarted)
            throw;
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { detail = e.Message });
    }
});
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDistributionApi();
app.Run();

public partial class ServerProgram
{
}
