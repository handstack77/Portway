using System.Security.Cryptography;
using System.Text;

namespace Portway.Core;

public static class LocalFiles
{
    public const int MaxTextBytes = 16 * 1024 * 1024;
    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
    public static string ValidateText(byte[] content)
    {
        if (content.Length > MaxTextBytes)
            throw new InvalidOperationException("편집기는 16 MiB 이하의 텍스트 파일을 지원합니다.");
        if (content.Contains((byte)0))
            throw new InvalidOperationException("바이너리 파일을 편집할 수 없습니다.");
        return new UTF8Encoding(false, true).GetString(content);
    }

    public static Entry Stat(string path)
    {
        FileSystemInfo f = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return new(f.Name, f.FullName, f is DirectoryInfo, f is FileInfo file ? file.Length : 0, f.LastWriteTimeUtc, "", f.LinkTarget != null);
    }

    public static Listing List(string? path)
    {
        var dir = new DirectoryInfo(string.IsNullOrWhiteSpace(path) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : path);
        var entries = new List<Entry>();
        foreach (var item in dir.EnumerateFileSystemInfos())
        {
            try
            {
                entries.Add(Stat(item.FullName));
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        return new(dir.FullName, dir.Parent?.FullName, entries.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static TextDocument Read(string path, string? encoding = null)
    {
        if (new FileInfo(path).Length > MaxTextBytes)
            throw new InvalidOperationException("내장 편집기 크기 제한 16 MiB를 초과합니다.");
        var bytes = File.ReadAllBytes(path);
        return TextCodec.Decode(bytes, encoding);
    }

    public static void Save(FileRequest request)
    {
        if (new FileInfo(request.Path).LinkTarget != null)
            throw new IOException("심볼릭 링크는 직접 편집할 수 없습니다.");
        var original = File.ReadAllBytes(request.Path);
        if (Hash(original) != request.Etag)
            throw new InvalidOperationException("파일이 변경되었습니다. 다시 열어 확인하세요.");
        var detected = TextCodec.Detect(original);
        var bytes = TextCodec.Encode(request.Content ?? "", request.Encoding ?? detected.Name, request.Bom ?? detected.Bom);
        var temp = request.Path + ".portway-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temp, File.GetUnixFileMode(request.Path));
            File.Move(temp, request.Path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static void Delete(string path)
    {
        var full = Path.GetFullPath(path);
        if (Path.GetPathRoot(full) == full || full == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            throw new IOException("루트 또는 홈 폴더는 삭제할 수 없습니다.");
        if (Directory.Exists(full))
            Directory.Delete(full, true);
        else
            File.Delete(full);
    }

    public static void Rename(FileRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Destination))
            throw new ArgumentException("새 경로가 필요합니다.");
        if (Directory.Exists(r.Path))
            Directory.Move(r.Path, r.Destination);
        else
            File.Move(r.Path, r.Destination, false);
    }
}
