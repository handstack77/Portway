using Portway.Core;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 업데이트 상태와 수동 확인·다운로드·적용. 기존 /api 그룹에 경로를 등록합니다.
    static void MapUpdates(RouteGroupBuilder api)
    {
        api.MapGet("/updates", (UpdateService s) => s.Status);
        api.MapPost("/updates/check", (UpdateService s, CancellationToken ct) => s.Check(ct));
        api.MapPost("/updates/download", (UpdateService s, CancellationToken ct) => s.Download(ct));
        api.MapPost("/updates/apply", async (UpdateService s, TransferQueue q, SyncService sync, LiveSyncService watches, ExternalEditorService editors, CancellationToken ct) =>
        {
            if (q.Active || sync.Active || watches.Active || editors.Active)
                throw new InvalidOperationException("전송과 동기화를 정지하고 외부 편집 세션을 종료한 뒤 업데이트하세요.");
            await s.Apply(ct);
            return Results.Ok();
        });
    }
}
