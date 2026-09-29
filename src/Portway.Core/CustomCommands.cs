namespace Portway.Core;

public static class CustomCommands
{
    public static string Expand(string template, string directory, IReadOnlyList<string> paths)
    {
        static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        if (template.Length > 32768)
            throw new ArgumentException("명령이 너무 깁니다.");
        if (template.Contains("{file}", StringComparison.Ordinal) && paths.Count != 1)
            throw new ArgumentException("{file} 명령은 파일 하나를 선택하세요.");
        if (template.Contains("{files}", StringComparison.Ordinal) && paths.Count == 0)
            throw new ArgumentException("파일을 선택하세요.");
        // 한 번만 치환합니다. 치환된 파일 이름에 {directory}가 포함돼도 다시 확장하지 않습니다.
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{(file|files|directory)\}", m => m.Groups[1].Value switch
        {
            "file" => Quote(paths[0]),
            "files" => string.Join(' ', paths.Select(Quote)),
            _ => Quote(directory)
        });
    }
}
