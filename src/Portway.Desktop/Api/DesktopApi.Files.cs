using Portway.Core;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 로컬·원격 파일 탐색과 파일 작업. 기존 /api 그룹에 경로를 등록합니다.
    static void MapFiles(RouteGroupBuilder api)
    {
        api.MapGet("/local", (string? path) => LocalFiles.List(path));
        api.MapPost("/local/mkdir", (FileRequest r) =>
        {
            Directory.CreateDirectory(r.Path);
            return Results.Ok();
        });
        api.MapPost("/local/rename", (FileRequest r) =>
        {
            LocalFiles.Rename(r);
            return Results.Ok();
        });
        api.MapPost("/local/delete", (FileRequest r) =>
        {
            LocalFiles.Delete(r.Path);
            return Results.Ok();
        });
        api.MapPost("/local/read", (FileRequest r) => LocalFiles.Read(r.Path, r.Encoding));
        api.MapPost("/local/write", (FileRequest r) =>
        {
            LocalFiles.Save(r);
            return LocalFiles.Read(r.Path, r.Encoding);
        });
        api.MapGet("/remote/{id}", (string id, string path, Connections c, CancellationToken ct) => c.Use(id, async fs => new Listing(RemotePaths.Normalize(path), RemotePaths.Parent(path), await fs.List(RemotePaths.Normalize(path), ct)), ct));
        api.MapPost("/remote/{id}/mkdir", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs =>
        {
            await fs.CreateDirectory(RemotePaths.Normalize(r.Path), ct);
            return Results.Ok();
        }, ct));
        api.MapPost("/remote/{id}/rename", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs =>
        {
            await fs.Move(r.Path, r.Destination ?? throw new ArgumentException("새 경로가 필요합니다."), ct);
            return Results.Ok();
        }, ct));
        api.MapPost("/remote/{id}/delete", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs =>
        {
            await TransferOperations.DeleteTree(fs, r.Path, ct);
            return Results.Ok();
        }, ct));
        api.MapPost("/remote/{id}/chmod", (string id, PermissionRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs =>
        {
            await RemoteFileOperations.Permissions(fs, r.Path, r.Octal, r.Recursive, r.Owner, r.Group, ct);
            return Results.Ok();
        }, ct));
        api.MapPost("/remote/{id}/copy", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs =>
        {
            await fs.Copy(r.Path, r.Destination ?? throw new ArgumentException("복사 경로가 필요합니다."), ct);
            return Results.Ok();
        }, ct));
        api.MapPost("/remote/{id}/link", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs =>
        {
            await fs.CreateLink(r.Path, r.Destination ?? throw new ArgumentException("링크 경로가 필요합니다."), ct);
            return Results.Ok();
        }, ct));
        api.MapPost("/remote/{id}/search", (string id, SearchRequest r, Connections c, CancellationToken ct) => c.Use(id, fs => RemoteFileOperations.Search(fs, r, ct), ct));
        api.MapPost("/remote/{id}/properties", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, fs => RemoteFileOperations.Properties(fs, r.Path, ct), ct));
        api.MapPost("/remote/{id}/checksum", (string id, ChecksumRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs => new { hash = await FileChecksums.Remote(fs, r.Path, r.Algorithm, ct) }, ct));
        api.MapPost("/local/checksum", async (ChecksumRequest r, CancellationToken ct) => new { hash = await FileChecksums.Local(r.Path, r.Algorithm, ct) });
        api.MapPost("/remote/{id}/command", (string id, CommandRequest r, Connections c, CancellationToken ct) => c.Use(id, async fs => new { output = await fs.Command(r.Command, ct) }, ct));
        api.MapPost("/remote/{id}/read", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, fs => RemoteTextFiles.Read(fs, r.Path, ct, r.Encoding), ct));
        api.MapPost("/remote/{id}/write", (string id, FileRequest r, Connections c, CancellationToken ct) => c.Use(id, fs => RemoteTextFiles.Write(fs, r, ct), ct));
    }
}
