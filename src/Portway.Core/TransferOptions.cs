using System.Globalization;
using System.Text.RegularExpressions;

namespace Portway.Core;

public sealed record TransferOptions
{
    public string FileMask { get; init; } = "";
    public bool ExcludeHidden { get; init; }
    public bool ExcludeEmptyDirectories { get; init; }
    public bool NewerOnly { get; init; }
    public string Mode { get; init; } = "binary";
    public string TextMask { get; init; } = "*.txt;*.csv;*.json;*.xml;*.html;*.css;*.js;*.cs;*.md;*.sh;*.yml;*.yaml;*.ini;*.config";
    public string RemoteNewline { get; init; } = "lf";
    public string NameCase { get; init; } = "none";
    public bool PreserveTimestamp { get; init; } = true;
    public bool PreserveReadOnly { get; init; }
    public string? Permissions { get; init; }
    public bool VerifyChecksum { get; init; }
    public bool RemoveSource { get; init; }
    public bool Resume { get; init; } = true;
    public int MaxRetries { get; init; } = 2;

    public void Validate()
    {
        if (Mode is not ("binary" or "text" or "automatic") || RemoteNewline is not ("lf" or "crlf") || NameCase is not ("none" or "lower" or "upper"))
            throw new ArgumentException("전송 설정이 올바르지 않습니다.");
        if (Permissions != null && !Regex.IsMatch(Permissions, "^[0-7]{3,4}$"))
            throw new ArgumentException("권한은 8진수 3–4자리입니다.");
        if (MaxRetries is < 0 or > 10)
            throw new ArgumentException("재시도는 0–10회입니다.");
        _ = new FileMask(FileMask);
        _ = new FileMask(TextMask);
    }

    public string TargetName(string name) => NameCase switch
    {
        "lower" => name.ToLowerInvariant(),
        "upper" => name.ToUpperInvariant(),
        _ => name
    };
}

public sealed class FileMask
{
    sealed record Rule(Regex Pattern, bool DirectoryOnly, bool FullPath, (string Operator, string Value)[] Constraints);
    readonly Rule[] include;
    readonly Rule[] exclude;
    public FileMask(string? mask)
    {
        if ((mask?.Length ?? 0) > 4096)
            throw new ArgumentException("파일 마스크가 너무 깁니다.");
        var parts = (mask ?? "").Split('|');
        if (parts.Length > 2)
            throw new ArgumentException("파일 마스크의 포함/제외 구분자 | 는 한 개만 사용할 수 있습니다.");
        include = Parse(parts[0]);
        exclude = parts.Length == 2 ? Parse(parts[1]) : [];
    }

    static Rule[] Parse(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(text =>
    {
        var constraints = Regex.Matches(text, @"(>=|<=|>|<)\s*([^<>]+)").Select(m => (m.Groups[1].Value, m.Groups[2].Value.Trim())).ToArray();
        var pos = text.IndexOfAny(['<', '>']);
        var glob = (pos < 0 ? text : text[..pos]).Trim().Replace('\\', '/');
        if (glob.Length == 0)
            glob = "*";
        var directory = glob.EndsWith('/');
        glob = glob.TrimEnd('/');
        var full = glob.Contains('/');
        if (glob == "*.*")
            glob = "*";
        var pattern = Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", ".*").Replace(@"\?", ".");
        if (glob == "*.")
            pattern = "[^.]*";
        return new Rule(new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)), directory, full, constraints);
    }).ToArray();
    public bool Matches(Entry entry, string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');
        bool Match(Rule r)
        {
            if (r.DirectoryOnly && !entry.IsDirectory)
                return false;
            if (!r.DirectoryOnly && entry.IsDirectory)
                return false;
            if (!r.Pattern.IsMatch(r.FullPath ? relativePath : entry.Name))
                return false;
            foreach (var (op, text) in r.Constraints)
            {
                long actual, expected;
                if (Regex.IsMatch(text, @"^\d+(\.\d+)?\s*[KMGT]?[Bb]?$", RegexOptions.IgnoreCase))
                {
                    var match = Regex.Match(text, @"^(\d+(?:\.\d+)?)\s*([KMGT]?)", RegexOptions.IgnoreCase);
                    var power = " KMGT".IndexOf(match.Groups[2].Value.ToUpperInvariant().FirstOrDefault(' '));
                    expected = checked((long)(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * Math.Pow(1024, power)));
                    actual = entry.Size;
                }
                else
                {
                    expected = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcTicks;
                    actual = entry.Modified.UtcTicks;
                }

                if (!(op switch
                {
                    ">" => actual > expected,
                    ">=" => actual >= expected,
                    "<" => actual < expected,
                    _ => actual <= expected
                }))
                    return false;
            }

            return true;
        }

        var applicable = include.Where(r => r.DirectoryOnly == entry.IsDirectory).ToArray();
        return !exclude.Any(Match) && (applicable.Length == 0 || applicable.Any(Match));
    }
}
