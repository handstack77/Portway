using Portway.Core;
using System.Reflection;

namespace Portway.Desktop;

public static partial class DesktopApi
{
    // 역할: 프로필·사이트·금고·설정과 저장된 사용자 명령. 기존 /api 그룹에 경로를 등록합니다.
    static void MapProfiles(RouteGroupBuilder api)
    {
        api.MapGet("/info", () => new
        {
            name = "Portway",
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            separator = Path.DirectorySeparatorChar,
            drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name)
        });
        api.MapGet("/sites", (ProfileStore p) => p.List());
        api.MapPost("/sites/import", (ImportRequest r, ProfileStore p) =>
        {
            var result = SiteArchive.Import(r.Content);
            return new
            {
                imported = p.ImportSites(result.Sites),
                skipped = result.Skipped
            };
        });
        api.MapPost("/sites/export", (ProfileStore p, SiteExportService s, CancellationToken ct) => s.Export(p, ct));
        api.MapPost("/sites", (Site s, ProfileStore p) =>
        {
            p.Save(s);
            return Results.Ok();
        });
        api.MapDelete("/sites/{id}", (string id, ProfileStore p) =>
        {
            p.Delete(id);
            return Results.Ok();
        });
        api.MapGet("/vault", (ProfileStore p) => p.Status());
        api.MapPost("/vault/unlock", (PasswordRequest r, ProfileStore p) =>
        {
            p.Unlock(r.Password);
            return p.Status();
        });
        api.MapPost("/vault/lock", (ProfileStore p) =>
        {
            p.Lock();
            return p.Status();
        });
        api.MapGet("/preferences", (ProfileStore p) => p.Preferences());
        api.MapPost("/commands/run", (CustomCommandRequest r, Connections c, ProfileStore p, CancellationToken ct) => c.Use(r.SessionId, async fs =>
        {
            var command = p.Preferences().Commands?.Single(x => x.Name == r.Name) ?? throw new KeyNotFoundException("저장된 명령이 없습니다.");
            if (!command.Remote)
                throw new NotSupportedException("사용자 명령은 SSH 서버에서 실행합니다.");
            return new
            {
                output = await fs.Command(CustomCommands.Expand(command.Template, r.Directory, r.Paths), ct)
            };
        }, ct));
        api.MapPut("/preferences", (Preferences r, ProfileStore p) =>
        {
            UpdateService.ValidateUrl(r.UpdateUrl);
            if (r.Theme is not ("light" or "dark" or "system"))
                throw new ArgumentException("테마는 light, dark, system 중 하나여야 합니다.");
            p.Preferences(r);
            return Results.Ok();
        });
    }
}
