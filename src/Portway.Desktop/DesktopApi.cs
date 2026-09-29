namespace Portway.Desktop;
/// <summary>Desktop API의 기능별 경로를 하나의 인증 대상 그룹에 등록합니다.</summary>
public static partial class DesktopApi
{
    public static void MapDesktopApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        MapProfiles(api);
        MapConnections(api);
        MapFiles(api);
        MapTransfers(api);
        MapSynchronization(api);
        MapTools(api);
        MapUpdates(api);
    }
}
