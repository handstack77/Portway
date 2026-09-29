using Portway.Core;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 외부 편집·휴지통·SSH 터미널. 기존 /api 그룹에 경로를 등록합니다.
    static void MapTools(RouteGroupBuilder api)
    {
        api.MapGet("/external-edits", (ExternalEditorService service) => service.List());
        api.MapPost("/external-edits/{sessionId}", (string sessionId, FileRequest request, ExternalEditorService service, CancellationToken ct) => service.Start(sessionId, request.Path, ct));
        api.MapPost("/external-edits/{id}/control", (string id, ActionRequest request, ExternalEditorService service) => service.Control(id, request.Action));
        api.MapGet("/trash", (TrashService s) => s.List());
        api.MapPost("/trash", (TrashRequest r, TrashService s, CancellationToken ct) => s.Put(r.SessionId, r.Path, ct));
        api.MapPost("/trash/{id}/restore", (string id, ActionRequest r, TrashService s, CancellationToken ct) => s.Restore(id, r.SessionId, ct));
        api.MapPost("/terminals", (TerminalRequest r, TerminalService s, CancellationToken ct) => s.Open(r.SessionId, r.Columns, r.Rows, ct));
        api.MapGet("/terminals/{id}", (string id, TerminalService s) => s.Read(id));
        api.MapPost("/terminals/{id}", (string id, TerminalInput r, TerminalService s) =>
        {
            s.Write(id, r.Data, r.Columns, r.Rows);
            return Results.Ok();
        });
        api.MapDelete("/terminals/{id}", (string id, TerminalService s) => s.Close(id));
    }
}
