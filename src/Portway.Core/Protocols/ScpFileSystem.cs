using System.Globalization;
using Renci.SshNet;

namespace Portway.Core.Protocols;
// POSIX 셸로 SCP 바이트를 전송하며 연결 시 GNU/BusyBox와 BSD의 stat 형식을 감지합니다.
public sealed class ScpFileSystem : IRemoteFileSystem
{
    readonly SshClient shell;
    readonly ScpClient scp;
    string statCommand = "stat -c '%s %Y %a'";
    public ScpFileSystem(Site site, IAuthenticationInteraction? interaction = null)
    {
        shell = SshConnection.Verify(new SshClient(SshConnection.Info(site, interaction)), site);
        scp = SshConnection.Verify(new ScpClient(SshConnection.Info(site, interaction), RemotePathTransformation.ShellQuote), site);
    }

    public Capabilities Capabilities => new(false, true, true, true, true, true);

    public async Task Connect(CancellationToken ct)
    {
        await shell.ConnectAsync(ct);
        await scp.ConnectAsync(ct);
        var kind = await Exec("if stat -c '%s' / >/dev/null 2>&1; then printf gnu; else stat -f '%z' / >/dev/null && printf bsd; fi", ct);
        statCommand = kind == "gnu" ? "stat -c '%s %Y %a'" : "stat -f '%z %m %Lp'";
    }

    async Task<string> Exec(string value, CancellationToken ct)
    {
        using var command = shell.CreateCommand(value);
        command.CommandTimeout = TimeSpan.FromSeconds(30);
        await command.ExecuteAsync(ct);
        if (command.ExitStatus != 0)
            throw new IOException(command.Error.Trim().Length > 0 ? command.Error : "원격 명령 실패");
        return command.Result;
    }

    static string Q(string value) => RemotePaths.ShellQuote(RemotePaths.Normalize(value));
    static Entry[] Parse(string text)
    {
        var fields = text.Split('\0');
        var list = new List<Entry>();
        for (var i = 0; i + 4 < fields.Length; i += 5)
        {
            var p = fields[i];
            list.Add(new(RemotePaths.Name(p), p, fields[i + 1] == "d", long.Parse(fields[i + 2], CultureInfo.InvariantCulture), DateTimeOffset.FromUnixTimeSeconds((long)double.Parse(fields[i + 3], CultureInfo.InvariantCulture)), fields[i + 4], fields[i + 1] == "l"));
        }

        return list.ToArray();
    }

    string Format => " -exec sh -c " + RemotePaths.ShellQuote("for p do if test -L \"$p\"; then kind=l; elif test -d \"$p\"; then kind=d; else kind=f; fi; fields=$(" + statCommand + " \"$p\") || exit; set -- $fields; printf '%s\\0%s\\0%s\\0%s\\0%s\\0' \"$p\" \"$kind\" \"$1\" \"$2\" \"$3\"; done") + " sh {} +";

    public async Task<Entry[]> List(string path, CancellationToken ct) => Parse(await Exec("LC_ALL=C find " + Q(path) + " -mindepth 1 -maxdepth 1" + Format, ct));
    public async Task<Entry?> Stat(string path, CancellationToken ct) => Parse(await Exec("if test -e " + Q(path) + " || test -L " + Q(path) + "; then LC_ALL=C find " + Q(path) + " -maxdepth 0" + Format + "; fi", ct)).FirstOrDefault();
    public async Task CreateDirectory(string path, CancellationToken ct) => await Exec("mkdir -- " + Q(path), ct);
    public async Task Delete(string path, bool directory, CancellationToken ct) => await Exec((directory ? "rmdir -- " : "rm -- ") + Q(path), ct);
    public async Task Move(string source, string destination, CancellationToken ct) => await Exec("if test -e " + Q(destination) + " || test -L " + Q(destination) + "; then exit 1; else mv -- " + Q(source) + " " + Q(destination) + "; fi", ct);
    public async Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        using var cancel = ct.Register(() => scp.Disconnect());
        void Handler(object? _, Renci.SshNet.Common.ScpDownloadEventArgs e) => progress(new(e.Downloaded, e.Size));
        scp.Downloading += Handler;
        try
        {
            await Task.Run(() => scp.Download(remote, new FileInfo(local)), ct);
        }
        finally
        {
            scp.Downloading -= Handler;
        }
    }

    public async Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        using var cancel = ct.Register(() => scp.Disconnect());
        void Handler(object? _, Renci.SshNet.Common.ScpUploadEventArgs e) => progress(new(e.Uploaded, e.Size));
        scp.Uploading += Handler;
        try
        {
            await Task.Run(() => scp.Upload(new FileInfo(local), remote), ct);
        }
        finally
        {
            scp.Uploading -= Handler;
        }
    }

    public async Task Chmod(string path, string octal, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(octal, "^[0-7]{3,4}$"))
            throw new ArgumentException("권한은 8진수 3–4자리입니다.");
        await Exec("chmod " + octal + " -- " + Q(path), ct);
    }

    public Task<string> Command(string command, CancellationToken ct) => Exec(command, ct);
    public async Task SetModified(string path, DateTimeOffset modified, CancellationToken ct) => await Exec("TZ=UTC touch -m -t " + modified.UtcDateTime.ToString("yyyyMMddHHmm.ss", CultureInfo.InvariantCulture) + " " + Q(path), ct);
    public async Task CreateLink(string target, string link, CancellationToken ct) => await Exec("ln -s " + RemotePaths.ShellQuote(target) + " " + Q(link), ct);
    public async Task SetOwner(string path, int? owner, int? group, CancellationToken ct)
    {
        if (owner is < 0 || group is < 0)
            throw new ArgumentException("소유자와 그룹 ID는 양수입니다.");
        if (owner == null && group == null)
            return;
        await Exec("chown " + (owner?.ToString(CultureInfo.InvariantCulture) ?? "") + (group == null ? "" : ":" + group.Value.ToString(CultureInfo.InvariantCulture)) + " " + Q(path), ct);
    }

    public ValueTask DisposeAsync()
    {
        scp.Dispose();
        shell.Dispose();
        return ValueTask.CompletedTask;
    }
}
