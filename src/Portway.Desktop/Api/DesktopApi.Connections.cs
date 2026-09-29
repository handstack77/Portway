using Portway.Core;
using Portway.Core.Protocols;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 서버 지문·인증 상호작용과 연결 수명. 기존 /api 그룹에 경로를 등록합니다.
    static void MapConnections(RouteGroupBuilder api)
    {
        api.MapGet("/authentication", (AuthenticationBroker b) => b.List());
        api.MapPost("/authentication/{id}", (string id, AuthenticationReply r, AuthenticationBroker b) =>
        {
            b.Respond(id, r.Answers);
            return Results.Ok();
        });
        api.MapPost("/fingerprint", async (Site s, AuthenticationBroker b, CancellationToken ct) => new { fingerprint = await SshConnection.Scan(s, ct, b) });
        api.MapPost("/certificate", (Site s, CancellationToken ct) => TlsOptions.Probe(s, ct));
        api.MapPost("/sessions", (Site s, Connections c, CancellationToken ct) => c.Connect(s, ct));
        api.MapDelete("/sessions/{id}", async (string id, Connections c) =>
        {
            await c.Disconnect(id);
            return Results.Ok();
        });
    }
}
