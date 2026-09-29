using Portway.Core;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 영속 전송 큐와 외부 드롭 준비. 기존 /api 그룹에 경로를 등록합니다.
    static void MapTransfers(RouteGroupBuilder api)
    {
        api.MapGet("/transfers", (TransferQueue q) => q.List());
        api.MapPost("/drops", (DropManifest r, DropStore s) => s.Create(r));
        api.MapPut("/drops/{id}/files/{index:int}", async (string id, int index, long offset, HttpRequest r, DropStore s, CancellationToken ct) => new { offset = await s.Append(id, index, offset, r.ContentLength ?? throw new ArgumentException("Content-Length가 필요합니다."), r.Body, ct) });
        api.MapPost("/drops/{id}/commit", async (string id, TransferRequest r, DropStore s, CancellationToken ct) => new { id = await s.Commit(id, r, ct) });
        api.MapDelete("/drops/{id}", async (string id, DropStore s, CancellationToken ct) =>
        {
            await s.Abandon(id, ct);
            return Results.Ok();
        });
        api.MapPost("/transfers", (TransferRequest r, TransferQueue q) => new { id = q.Add(r) });
        api.MapPost("/transfers/{id}", (string id, ActionRequest r, TransferQueue q) =>
        {
            q.Control(id, r.Action, r.SessionId);
            return Results.Ok();
        });
        api.MapDelete("/transfers", (TransferQueue q) =>
        {
            q.Clear();
            return Results.Ok();
        });
    }
}
