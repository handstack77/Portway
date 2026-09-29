using Portway.Core;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 동기화 계획과 지속 동기화. 기존 /api 그룹에 경로를 등록합니다.
    static void MapSynchronization(RouteGroupBuilder api)
    {
        api.MapPost("/sync/preview", (SyncRequest r, SyncService s, CancellationToken ct) => s.Preview(r, ct));
        api.MapPost("/sync/{id}/apply", (string id, SyncApplyRequest? request, SyncService s) => s.Apply(id, request?.Selected, request?.Resolutions));
        api.MapPost("/sync/{id}/cancel", (string id, SyncService s) =>
        {
            s.Cancel(id);
            return Results.Ok();
        });
        api.MapGet("/sync/{id}", (string id, SyncService s) => s.Status(id));
        api.MapGet("/watches", (LiveSyncService service) => service.List());
        api.MapPost("/watches", (WatchRequest request, LiveSyncService service) => new { id = service.Add(request) });
        api.MapPost("/watches/{id}", (string id, ActionRequest request, LiveSyncService service) => service.Control(id, request.Action, request.SessionId));
    }
}
