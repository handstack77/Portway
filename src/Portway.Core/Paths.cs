namespace Portway.Core;

public static class RemotePaths
{
    public static string Normalize(string path)
    {
        if (path.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("경로에 제어 문자를 사용할 수 없습니다.");
        var stack = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
            else
                stack.Add(part);
        }

        return "/" + string.Join('/', stack);
    }

    public static string Join(string parent, string name) => Normalize(parent.TrimEnd('/') + "/" + name);
    public static string Name(string path) => Normalize(path).Split('/').Last();
    public static string? Parent(string path) => Normalize(path) == "/" ? null : Normalize(path)[..Normalize(path).LastIndexOf('/')] is { Length: > 0 } p ? p : "/";
    public static string SafeLocalChild(string parent, string name)
    {
        if (name is "" or "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            throw new IOException("안전하지 않은 원격 파일 이름: " + name);
        var root = Path.GetFullPath(parent) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(parent, name));
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("경로가 대상 폴더를 벗어납니다.");
        return full;
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}

public static class StreamCopy
{
    public static async Task Copy(Stream input, Stream output, long offset, long total, Action<TransferProgress> progress, CancellationToken ct)
    {
        var buffer = new byte[128 * 1024];
        long count = offset;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            count += read;
            progress(new(count, total));
        }

        await output.FlushAsync(ct);
    }
}
