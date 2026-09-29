namespace Portway.Desktop;
// 기존 DesktopApi 중첩 타입과 JSON 필드의 호환성을 유지합니다.
public static partial class DesktopApi
{
    public record PasswordRequest(string Password);
    public record ActionRequest(string Action, string? SessionId = null);
    public record CommandRequest(string Command);
    public record PermissionRequest(string Path, string Octal, bool Recursive = false, int? Owner = null, int? Group = null);
    public record TrashRequest(string Path, string? SessionId = null);
    public record TerminalRequest(string SessionId, uint Columns = 100, uint Rows = 30);
    public record TerminalInput(string Data = "", uint? Columns = null, uint? Rows = null);
    public record ChecksumRequest(string Path, string Algorithm = "sha256");
    public record CustomCommandRequest(string SessionId, string Name, string Directory, string[] Paths);
    public record ImportRequest(string Content);
    public record AuthenticationReply(string[]? Answers);
    public record SyncApplyRequest(string[]? Selected = null, Dictionary<string, string>? Resolutions = null);
}
