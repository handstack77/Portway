using System.Collections.Concurrent;
using System.Text;
using Portway.Core;
using Portway.Core.Protocols;
using Renci.SshNet;

namespace Portway.Desktop;

public sealed class TerminalService(Connections connections, IAuthenticationInteraction interaction) : IAsyncDisposable
{
    sealed class Terminal(SshRoute route, SshClient client, ShellStream stream)
    {
        public SshRoute Route = route;
        public SshClient Client = client;
        public ShellStream Stream = stream;
        public DateTimeOffset Used = DateTimeOffset.UtcNow;
        public readonly object Gate = new();
    }

    readonly ConcurrentDictionary<string, Terminal> terminals = new();
    public async Task<object> Open(string sessionId, uint columns, uint rows, CancellationToken ct)
    {
        foreach (var item in terminals.Where(t => t.Value.Used < DateTimeOffset.UtcNow.AddHours(-2)))
            await Close(item.Key);
        if (terminals.Count >= 10)
            throw new IOException("터미널은 최대 10개입니다.");
        ValidateSize(columns, rows);
        var site = connections.Get(sessionId).Site;
        if (site.EncryptFiles || site.EncryptionKey != null)
            throw new NotSupportedException("암호화 파일 세션은 복호화된 경로의 셸 명령을 지원하지 않습니다. 터미널에는 별도의 일반 SSH 연결을 사용하세요.");
        if (site.Protocol is not ("sftp" or "scp"))
            throw new NotSupportedException("SSH 연결에서 터미널을 사용할 수 있습니다.");
        var route = await SshRoute.Open(site, interaction, ct);
        SshClient? client = null;
        try
        {
            client = SshConnection.Verify(new SshClient(SshConnection.Info(route.Target, interaction)), route.Target);
            await client.ConnectAsync(ct);
            var stream = client.CreateShellStream("xterm-256color", columns, rows, 0, 0, 65536);
            var id = Guid.NewGuid().ToString("N");
            terminals[id] = new(route, client, stream);
            return new
            {
                id
            };
        }
        catch
        {
            client?.Dispose();
            await route.DisposeAsync();
            throw;
        }
    }

    static void ValidateSize(uint columns, uint rows)
    {
        if (columns is < 10 or > 500 || rows is < 2 or > 300)
            throw new ArgumentException("터미널 크기가 올바르지 않습니다.");
    }

    Terminal Get(string id) => terminals.TryGetValue(id, out var t) ? t : throw new KeyNotFoundException("터미널이 닫혔습니다.");
    public object Read(string id)
    {
        var t = Get(id);
        lock (t.Gate)
        {
            t.Used = DateTimeOffset.UtcNow;
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (t.Stream.DataAvailable && output.Length < 256 * 1024)
            {
                var count = t.Stream.Read(buffer, 0, buffer.Length);
                if (count == 0)
                    break;
                output.Write(buffer, 0, count);
            }

            return new
            {
                data = Convert.ToBase64String(output.ToArray()),
                connected = t.Client.IsConnected && t.Stream.CanRead
            };
        }
    }

    public void Write(string id, string data, uint? columns, uint? rows)
    {
        if (data.Length > 65536)
            throw new ArgumentException("터미널 입력 크기 초과");
        var t = Get(id);
        lock (t.Gate)
        {
            t.Used = DateTimeOffset.UtcNow;
            if (columns != null && rows != null)
            {
                ValidateSize(columns.Value, rows.Value);
                t.Stream.ChangeWindowSize(columns.Value, rows.Value, 0, 0);
            }

            if (data.Length > 0)
                t.Stream.Write(data);
        }
    }

    public async Task Close(string id)
    {
        if (terminals.TryRemove(id, out var t))
        {
            lock (t.Gate)
            {
                t.Stream.Dispose();
                t.Client.Dispose();
            }

            await t.Route.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in terminals.Keys)
            await Close(id);
    }
}
