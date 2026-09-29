using System.Text;
using Photino.NET;
using Portway.Core;

namespace Portway.Desktop;

public sealed class SiteExportService
{
    readonly SemaphoreSlim gate = new(1, 1);
    public PhotinoWindow? Window { private get; set; }

    public record Result(int Count, string FileName, bool Cancelled = false, string? Path = null, string? Content = null);
    public async Task<Result> Export(ProfileStore profiles, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var sites = profiles.List();
            if (sites.Length == 0)
                throw new InvalidOperationException("내보낼 사이트가 없습니다. 사이트를 먼저 저장하세요.");
            var content = SiteArchive.Export(sites);
            var fileName = "Portway-sites-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
            var window = Window;
            // 브라우저 검증 호스트에서는 웹 다운로드를 사용하고 데스크톱에서는 OS 저장 창을 사용합니다.
            if (window == null)
                return new(sites.Length, fileName, Content: content);
            // Photino의 기본 경로는 기존 폴더여야 하며 없는 파일 경로를 주면 저장 창이 열리지 않습니다.
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!Directory.Exists(folder))
                folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = await window.ShowSaveFileAsync("사이트 내보내기", folder, [("Portway 사이트 JSON", ["json"])]);
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
                return new(sites.Length, fileName, Cancelled: true);
            if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("내보내기 파일의 확장자는 .json으로 지정하세요.");
            Save(path, content, profiles.DataPath);
            return new(sites.Length, Path.GetFileName(path), Path: path);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static void Save(string path, string content, string profileRoot)
    {
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileRoot));
        if (full.Equals(root, comparison) || full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("앱의 프로필 폴더 밖에 내보내기 파일을 저장하세요.");
        if (new FileInfo(full).LinkTarget != null)
            throw new IOException("심볼릭 링크에는 사이트를 내보낼 수 없습니다.");
        var temp = full + ".portway-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, full, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
