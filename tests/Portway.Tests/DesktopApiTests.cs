using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Portway.Core;
using Portway.Desktop;

namespace Portway.Tests;

public sealed class DesktopApiTests : IAsyncLifetime
{
    const string Token = "desktop-api-test-token-at-least-32-characters";
    readonly string root = Path.Combine(Path.GetTempPath(), "portway-api-" + Guid.NewGuid().ToString("N"));
    WebApplication app = null!;
    HttpClient client = null!;
    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Portway:DataPath"] = Path.Combine(root, "profile");
        builder.WebHost.UseTestServer();
        builder.Services.AddDesktopServices();
        app = builder.Build();
        app.UseDesktopSecurity(Token);
        app.MapDesktopApi();
        await app.StartAsync();
        client = app.GetTestClient();
        client.BaseAddress = new Uri("http://127.0.0.1");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    [Fact]
    public async Task SecurityRejectsInvalidTokenHostAndOriginBeforeApiHandlers()
    {
        using var anonymous = app.GetTestClient();
        anonymous.BaseAddress = client.BaseAddress;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/info")).StatusCode);
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/info")).StatusCode);
        using var wrongHost = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        wrongHost.Headers.Host = "untrusted.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrongHost)).StatusCode);
        using var wrongOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        wrongOrigin.Headers.Add("Origin", "https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrongOrigin)).StatusCode);
        using var valid = new HttpRequestMessage(HttpMethod.Get, "/api/info");
        valid.Headers.Add("Origin", "http://127.0.0.1");
        using var response = await client.SendAsync(valid);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("worker-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task ProfileAndToolsEndpointsResolveSharedServicesAfterSplittingRoutes()
    {
        var site = new Site
        {
            Name = "경로 분리 검증",
            Host = "test.invalid",
            Username = "test"
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sites", site)).StatusCode);
        var sites = await client.GetFromJsonAsync<Site[]>("/api/sites");
        Assert.Equal(site.Id, Assert.Single(sites!).Id);
        foreach (var path in new[]
        {
            "/vault",
            "/preferences",
            "/authentication",
            "/transfers",
            "/watches",
            "/external-edits",
            "/trash",
            "/updates"
        }

        )
        {
            using var response = await client.GetAsync("/api" + path);
            Assert.True(response.IsSuccessStatusCode, path + ": " + await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/sites/" + site.Id)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<Site[]>("/api/sites"))!);
    }

    [Fact]
    public async Task LocalTextEditingRetainsConflictProtectionAndMissingSessionErrors()
    {
        var file = Path.Combine(root, "편집.txt");
        await File.WriteAllTextAsync(file, "처음 내용");
        using var readResponse = await client.PostAsJsonAsync("/api/local/read", new FileRequest(file));
        var document = await readResponse.Content.ReadFromJsonAsync<TextDocument>();
        Assert.NotNull(document);
        using var saved = await client.PostAsJsonAsync("/api/local/write", new FileRequest(file, Content: "저장 내용", Etag: document.Etag));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("저장 내용", await File.ReadAllTextAsync(file));
        using var conflict = await client.PostAsJsonAsync("/api/local/write", new FileRequest(file, Content: "이전 버전 덮어쓰기", Etag: document.Etag));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var missing = await client.GetAsync("/api/remote/missing?path=/");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("연결이 종료되었습니다.", problem.GetProperty("detail").GetString());
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        // 이 테스트에서 생성한 절대 임시 경로만 정리합니다.
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
