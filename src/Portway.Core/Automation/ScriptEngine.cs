using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Portway.Core.Automation;

public sealed class ScriptEngine(TextWriter output) : IAsyncDisposable
{
    readonly Dictionary<string, Session> sessions = new();
    string active = "";
    string remotePath = "/", localPath = Environment.CurrentDirectory;
    bool continueOnError, failOnNoMatch;
    int failures;
    Session Current => sessions.TryGetValue(active, out var session) ? session : throw new InvalidOperationException("연결된 세션이 없습니다.");

    public static string[] Tokenize(string line)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        bool quoted = false, started = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    token.Append('"');
                    i++;
                }
                else
                    quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                {
                    result.Add(token.ToString());
                    token.Clear();
                    started = false;
                }
            }
            else
            {
                token.Append(c);
                started = true;
            }
        }

        if (quoted)
            throw new ArgumentException("따옴표가 닫히지 않았습니다.");
        if (started)
            result.Add(token.ToString());
        return result.ToArray();
    }

    string Remote(string path) => path.StartsWith('/') ? RemotePaths.Normalize(path) : RemotePaths.Join(remotePath, path);
    string Local(string path) => Path.GetFullPath(path, localPath);
    // 한글 로그는 읽을 수 있게 출력하며 JSON과 HTML 민감 문자의 기본 이스케이프는 유지합니다.
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };
    public async Task<int> Run(IEnumerable<string> lines, CancellationToken ct = default)
    {
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            try
            {
                if (!await Execute(Tokenize(line), line.TrimStart(), ct))
                    break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                failures++;
                await output.WriteLineAsync(JsonSerializer.Serialize(new { error = e.Message }, Json));
                if (!continueOnError)
                    break;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    async Task<string[]> Expand(string path, bool local, CancellationToken ct)
    {
        if (!path.Contains('*') && !path.Contains('?'))
            return [local ? Local(path) : Remote(path)];
        var full = local ? Local(path) : Remote(path);
        var parent = local ? Path.GetDirectoryName(full)! : RemotePaths.Parent(full)!;
        var name = local ? Path.GetFileName(full) : RemotePaths.Name(full);
        var entries = local ? LocalFiles.List(parent).Entries : await Current.ListDirectory(parent, ct);
        var mask = new FileMask(name);
        var result = entries.Where(e => !e.IsDirectory && mask.Matches(e, e.Name)).Select(e => e.Path).ToArray();
        if (result.Length == 0 && failOnNoMatch)
            throw new FileNotFoundException("일치하는 파일이 없습니다: " + path);
        return result;
    }

    async Task<bool> Execute(string[] tokens, string raw, CancellationToken ct)
    {
        if (tokens.Length == 0)
            return true;
        var command = tokens[0].ToLowerInvariant();
        var rest = tokens.Skip(1).ToArray();
        if (command == "call")
        {
            await output.WriteLineAsync(await Current.ExecuteCommand(raw[4..].TrimStart(), ct));
            return true;
        }

        var switches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var args = new List<string>();
        bool switchesEnded = false;
        foreach (var t in rest)
        {
            if (t == "--")
            {
                switchesEnded = true;
                continue;
            }

            if (t.StartsWith('-') && !switchesEnded)
            {
                var p = t.IndexOf('=');
                var key = p < 0 ? t[1..] : t[1..p];
                if (!switches.TryAdd(key, p < 0 ? "on" : t[(p + 1)..]))
                    throw new ArgumentException("옵션이 중복되었습니다: " + key);
            }
            else
                args.Add(t);
        }

        void Allow(params string[] allowed)
        {
            var unknown = switches.Keys.Except(allowed, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (unknown != null)
                throw new ArgumentException("지원하지 않는 옵션입니다: -" + unknown);
        }

        string Arg(int i) => i < args.Count ? args[i] : throw new ArgumentException("명령에 필요한 인수가 없습니다: " + command);
        string? Value(string key, string? fallback) => switches.TryGetValue(key, out var value) ? value : fallback;
        TransferOptions Options() => new()
        {
            FileMask = switches.GetValueOrDefault("filemask", ""),
            Mode = switches.GetValueOrDefault("transfer", "binary") is "ascii" ? "text" : switches.GetValueOrDefault("transfer", "binary"),
            Permissions = switches.GetValueOrDefault("permissions"),
            PreserveTimestamp = !switches.ContainsKey("nopreservetime"),
            RemoveSource = switches.ContainsKey("delete"),
            NewerOnly = switches.ContainsKey("neweronly"),
            Resume = switches.GetValueOrDefault("resumesupport", "on") != "off",
            VerifyChecksum = switches.ContainsKey("verify")
        };
        switch (command)
        {
            case "open":
                Allow("hostkey", "username", "password", "passwordenv", "privatekey", "passphraseenv", "certificate", "name", "bucket", "region");
                Site site;
                if (Arg(0).EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    site = JsonSerializer.Deserialize<Site>(await File.ReadAllTextAsync(Local(Arg(0)), ct), Json) ?? throw new ArgumentException("사이트 JSON 형식이 올바르지 않습니다.");
                else
                {
                    var uri = new Uri(Arg(0));
                    var user = uri.UserInfo.Split(':', 2);
                    site = new()
                    {
                        Protocol = uri.Scheme,
                        Host = uri.Host,
                        Port = uri.IsDefaultPort ? 0 : uri.Port,
                        Username = user.Length > 0 ? Uri.UnescapeDataString(user[0]) : "",
                        Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
                        RemotePath = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : Uri.UnescapeDataString(uri.AbsolutePath)
                    };
                }

                site = site with
                {
                    Fingerprint = Value("hostkey", site.Fingerprint),
                    Username = switches.GetValueOrDefault("username", site.Username),
                    Password = switches.TryGetValue("passwordenv", out var env) ? Environment.GetEnvironmentVariable(env) ?? throw new ArgumentException("암호를 담은 환경 변수가 없습니다.") : Value("password", site.Password),
                    PrivateKeyPath = Value("privatekey", site.PrivateKeyPath),
                    Passphrase = switches.TryGetValue("passphraseenv", out var passenv) ? Environment.GetEnvironmentVariable(passenv) : site.Passphrase,
                    TlsFingerprint = Value("certificate", site.TlsFingerprint),
                    Bucket = switches.GetValueOrDefault("bucket", site.Bucket),
                    Region = switches.GetValueOrDefault("region", site.Region)
                };
                var name = switches.GetValueOrDefault("name", (sessions.Count + 1).ToString());
                if (sessions.ContainsKey(name))
                    throw new ArgumentException("같은 이름의 세션이 이미 있습니다.");
                var session = new Session();
                await session.Open(site, ct);
                sessions[name] = session;
                active = name;
                remotePath = site.RemotePath;
                break;
            case "close":
                Allow();
                await Current.Close();
                sessions.Remove(active);
                active = sessions.Keys.LastOrDefault() ?? "";
                break;
            case "session":
                Allow();
                if (args.Count == 0)
                    await output.WriteLineAsync(string.Join('\n', sessions.Keys));
                else
                {
                    if (!sessions.ContainsKey(Arg(0)))
                        throw new ArgumentException("알 수 없는 세션입니다.");
                    active = Arg(0);
                }

                break;
            case "exit":
            case "bye":
                Allow();
                return false;
            case "option":
                Allow();
                switch (Arg(0))
                {
                    case "batch":
                        if (Arg(1) is not ("abort" or "continue"))
                            throw new ArgumentException("batch 옵션은 abort 또는 continue로 지정하세요.");
                        continueOnError = Arg(1) == "continue";
                        break;
                    case "confirm":
                        if (Arg(1) != "off")
                            throw new ArgumentException("자동화에서는 confirm off 설정이 필요합니다.");
                        break;
                    case "failonnomatch":
                        failOnNoMatch = Arg(1) == "on";
                        break;
                    default:
                        throw new ArgumentException("지원하지 않는 설정입니다: " + Arg(0));
                }

                break;
            case "echo":
                Allow();
                await output.WriteLineAsync(string.Join(' ', args));
                break;
            case "pwd":
                Allow();
                await output.WriteLineAsync(remotePath);
                break;
            case "lpwd":
                Allow();
                await output.WriteLineAsync(localPath);
                break;
            case "lcd":
                Allow();
                var dir = Local(Arg(0));
                if (!Directory.Exists(dir))
                    throw new DirectoryNotFoundException(dir);
                localPath = dir;
                break;
            case "cd":
                Allow();
                var target = Remote(Arg(0));
                if ((await Current.GetFileInfo(target, ct))?.IsDirectory != true)
                    throw new DirectoryNotFoundException(target);
                remotePath = target;
                break;
            case "ls":
                Allow();
                await output.WriteLineAsync(JsonSerializer.Serialize(await Current.ListDirectory(args.Count == 0 ? remotePath : Remote(Arg(0)), ct), Json));
                break;
            case "lls":
                Allow();
                await output.WriteLineAsync(JsonSerializer.Serialize(LocalFiles.List(args.Count == 0 ? localPath : Local(Arg(0))).Entries, Json));
                break;
            case "mkdir":
                Allow();
                await Current.CreateDirectory(Remote(Arg(0)), ct);
                break;
            case "rm":
                Allow();
                foreach (var arg in args)
                    await Current.RemoveFiles(await Expand(arg, false, ct), ct);
                break;
            case "rmdir":
                Allow();
                await Current.Use(async fs =>
                {
                    await fs.Delete(Remote(Arg(0)), true, ct);
                    return true;
                }, ct);
                break;
            case "mv":
                Allow();
                await Current.MoveFile(Remote(Arg(0)), Remote(Arg(1)), ct);
                break;
            case "cp":
                Allow();
                await Current.DuplicateFile(Remote(Arg(0)), Remote(Arg(1)), ct);
                break;
            case "ln":
                Allow();
                await Current.CreateSymbolicLink(Remote(Arg(0)), Remote(Arg(1)), ct);
                break;
            case "chmod":
                Allow("recursive");
                var chmodPaths = await Expand(Arg(1), false, ct);
                await Current.Use(async fs =>
                {
                    foreach (var path in chmodPaths)
                        await RemoteFileOperations.Permissions(fs, path, Arg(0), switches.ContainsKey("recursive"), null, null, ct);
                    return true;
                }, ct);
                break;
            case "checksum":
                Allow();
                await output.WriteLineAsync(await Current.CalculateFileChecksum(Remote(Arg(1)), Arg(0), ct));
                break;
            case "put":
            case "get":
                Allow("filemask", "transfer", "permissions", "preservetime", "nopreservetime", "delete", "neweronly", "resumesupport", "verify");
                var upload = command == "put";
                var source = await Expand(Arg(0), upload, ct);
                var destination = args.Count > 1 ? (upload ? Remote(Arg(1)) : Local(Arg(1))) : (upload ? remotePath : localPath);
                if (upload)
                    await Current.PutFiles(source, destination, Options(), ct: ct);
                else
                    await Current.GetFiles(source, destination, Options(), ct: ct);
                break;
            case "synchronize":
            case "keepuptodate":
                Allow("filemask", "transfer", "permissions", "delete", "criteria", "interval");
                var watching = command == "keepuptodate";
                var direction = watching ? "upload" : Arg(0) switch
                {
                    "remote" => "upload",
                    "local" => "download",
                    "both" => "both",
                    _ => throw new ArgumentException("동기화 방향은 remote, local 또는 both로 지정하세요.")
                };
                var offset = watching ? 0 : 1;
                var req = new SyncRequest("script", args.Count > offset ? Local(args[offset]) : localPath, args.Count > offset + 1 ? Remote(args[offset + 1]) : remotePath, direction, switches.ContainsKey("delete"), Options() with { RemoveSource = false }, switches.GetValueOrDefault("criteria", "checksum") switch
                {
                    "either" => "time-size",
                    var value => value
                });
                do
                {
                    await Current.SynchronizeDirectories(req, ct);
                    if (watching)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(int.Parse(switches.GetValueOrDefault("interval", "10")), 2, 86400)), ct);
                }
                while (watching);
                break;
            default:
                throw new ArgumentException("알 수 없는 명령입니다: " + command);
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.Values)
            await session.DisposeAsync();
    }
}
