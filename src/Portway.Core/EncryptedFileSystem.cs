using System.Security.Cryptography;

namespace Portway.Core;

public sealed class EncryptedFileSystem(IRemoteFileSystem inner, string encryptionKey) : IRemoteFileSystem
{
    readonly byte[] key = WinScpEncryption.ParseKey(encryptionKey);
    public Capabilities Capabilities => inner.Capabilities with
    {
        Resume = false,
        Commands = false,
        Links = false
    };

    public Task Connect(CancellationToken ct) => inner.Connect(ct);
    string Name(Entry e) => WinScpEncryption.DecryptName(e.Name, key);
    Entry Logical(Entry e, string parent)
    {
        var name = Name(e);
        var encrypted = e.Name.EndsWith(WinScpEncryption.Extension, StringComparison.Ordinal);
        return e with
        {
            Name = name,
            Path = RemotePaths.Join(parent, name),
            Size = !e.IsDirectory && encrypted && e.Size > 0 ? Math.Max(0, e.Size - 32) : e.Size
        };
    }

    async Task<string> Resolve(string path, bool create, bool encrypt, CancellationToken ct)
    {
        var parts = RemotePaths.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "/";
        for (var i = 0; i < parts.Length; i++)
        {
            var matches = (await inner.List(current, ct)).Where(e => Name(e) == parts[i]).ToArray();
            if (matches.Length > 1)
                throw new IOException("복호화한 파일 이름이 중복됩니다: " + path);
            if (matches.Length == 1)
            {
                if (matches[0].IsLink)
                    throw new IOException("암호화 세션에서 심볼릭 링크는 사용할 수 없습니다.");
                current = matches[0].Path;
            }
            else if (create && i == parts.Length - 1)
                current = RemotePaths.Join(current, encrypt ? WinScpEncryption.EncryptName(parts[i], key) : parts[i]);
            else
                throw new FileNotFoundException(path);
        }

        return current;
    }

    public async Task<Entry[]> List(string path, CancellationToken ct)
    {
        var physical = await Resolve(path, false, false, ct);
        var entries = (await inner.List(physical, ct)).Select(e => Logical(e, path)).ToArray();
        if (entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new IOException("복호화한 파일 이름이 중복됩니다.");
        return entries;
    }

    public async Task<Entry?> Stat(string path, CancellationToken ct)
    {
        if (RemotePaths.Normalize(path) == "/")
            return new("", "/", true, 0, DateTimeOffset.UnixEpoch);
        try
        {
            var physical = await Resolve(path, false, false, ct);
            var entry = await inner.Stat(physical, ct);
            return entry == null ? null : Logical(entry, RemotePaths.Parent(path)!);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task CreateDirectory(string path, CancellationToken ct) => await inner.CreateDirectory(await Resolve(path, true, true, ct), ct);
    public async Task Delete(string path, bool directory, CancellationToken ct) => await inner.Delete(await Resolve(path, false, false, ct), directory, ct);
    public async Task Move(string source, string destination, CancellationToken ct)
    {
        if (await Stat(destination, ct) != null)
            throw new IOException("대상이 이미 존재합니다.");
        var actual = await Resolve(source, false, false, ct);
        await inner.Move(actual, await Resolve(destination, true, actual.EndsWith(WinScpEncryption.Extension, StringComparison.Ordinal), ct), ct);
    }

    public async Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        if (offset != 0)
            throw new NotSupportedException("암호화 파일은 처음부터 재전송합니다.");
        var physical = await Resolve(remote, false, false, ct);
        if (!physical.EndsWith(WinScpEncryption.Extension, StringComparison.Ordinal))
        {
            await inner.Download(physical, local, 0, progress, ct);
            return;
        }

        var temp = Path.GetTempFileName();
        try
        {
            await inner.Download(physical, temp, 0, p => progress(new(Math.Max(0, p.Bytes - 32), Math.Max(0, p.Total - 32))), ct);
            await WinScpEncryption.File(temp, local, key, true, ct);
        }
        finally
        {
            System.IO.File.Delete(temp);
        }
    }

    public async Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        if (offset != 0)
            throw new NotSupportedException("암호화 파일은 처음부터 재전송합니다.");
        var encrypt = true;
        var original = remote;
        foreach (var marker in new[]
        {
            ".portway-part-",
            ".portway-edit-",
            ".portway-copy-"
        }

        )
        {
            var index = original.LastIndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
                original = original[..index];
        }

        try
        {
            encrypt = (await Resolve(original, false, false, ct)).EndsWith(WinScpEncryption.Extension, StringComparison.Ordinal);
        }
        catch (FileNotFoundException)
        {
        }

        var physical = await Resolve(remote, true, encrypt, ct);
        if (!encrypt)
        {
            await inner.Upload(local, physical, 0, progress, ct);
            return;
        }

        var temp = Path.GetTempFileName();
        try
        {
            await WinScpEncryption.File(local, temp, key, false, ct);
            await inner.Upload(temp, physical, 0, p => progress(new(Math.Max(0, p.Bytes - 32), Math.Max(0, p.Total - 32))), ct);
        }
        finally
        {
            System.IO.File.Delete(temp);
        }
    }

    public async Task Chmod(string path, string octal, CancellationToken ct) => await inner.Chmod(await Resolve(path, false, false, ct), octal, ct);
    public async Task SetOwner(string path, int? owner, int? group, CancellationToken ct) => await inner.SetOwner(await Resolve(path, false, false, ct), owner, group, ct);
    public async Task SetModified(string path, DateTimeOffset time, CancellationToken ct) => await inner.SetModified(await Resolve(path, false, false, ct), time, ct);
    public async ValueTask DisposeAsync()
    {
        CryptographicOperations.ZeroMemory(key);
        await inner.DisposeAsync();
    }
}
