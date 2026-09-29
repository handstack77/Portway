namespace Portway.Core;

public record SearchRequest(string Path, string Mask = "*", string? Text = null, bool CaseSensitive = false, string Encoding = "utf-8", int MaxResults = 1000);
public record SearchResult(Entry[] Entries, bool Truncated, int Scanned);
public static class RemoteFileOperations
{
    public static async Task Copy(IRemoteFileSystem remote, string source, string destination, CancellationToken ct, int depth = 0)
    {
        if (remote is not GuardedRemote)
            remote = new GuardedRemote(remote);
        source = RemotePaths.Normalize(source);
        destination = RemotePaths.Normalize(destination);
        if (depth > 64 || source == destination || destination.StartsWith(source.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new IOException("잘못된 복사 대상 또는 폴더 깊이입니다.");
        if (await remote.Stat(destination, ct) != null)
            throw new IOException("대상이 이미 존재합니다.");
        var entry = await remote.Stat(source, ct) ?? throw new FileNotFoundException(source);
        if (entry.IsLink)
            throw new IOException("링크는 링크 생성 기능으로 복사하세요.");
        if (entry.IsDirectory)
        {
            await TransferOperations.EnsureDirectory(remote, destination, ct);
            foreach (var child in await remote.List(source, ct))
                await Copy(remote, child.Path, RemotePaths.Join(destination, child.Name), ct, depth + 1);
        }
        else
        {
            var local = Path.GetTempFileName();
            var temporary = destination + ".portway-copy-" + Guid.NewGuid().ToString("N");
            try
            {
                await remote.Download(source, local, 0, _ =>
                {
                }, ct);
                await remote.Upload(local, temporary, 0, _ =>
                {
                }, ct);
                if (await FileChecksums.Local(local, "sha256", ct) != await FileChecksums.Remote(remote, temporary, "sha256", ct))
                    throw new IOException("복사 검증 실패");
                await remote.Move(temporary, destination, ct);
            }
            finally
            {
                File.Delete(local);
            }
        }

        if (remote.Capabilities.Permissions && System.Text.RegularExpressions.Regex.IsMatch(entry.Permissions, "^[0-7]{3,4}$"))
            await remote.Chmod(destination, entry.Permissions, ct);
        if (remote.Capabilities.Timestamps)
            await remote.SetModified(destination, entry.Modified, ct);
    }

    public static async Task<SearchResult> Search(IRemoteFileSystem remote, SearchRequest request, CancellationToken ct)
    {
        if (request.MaxResults is < 1 or > 100000)
            throw new ArgumentException("검색 한도는 1–100,000입니다.");
        var result = new List<Entry>();
        var mask = new FileMask(request.Mask);
        var scanned = 0;
        var truncated = false;
        async Task Scan(string directory, string prefix, int depth)
        {
            if (depth > 64)
                throw new IOException("검색 깊이를 초과했습니다.");
            foreach (var entry in await remote.List(directory, ct))
            {
                ct.ThrowIfCancellationRequested();
                scanned++;
                var relative = prefix + entry.Name;
                var included = mask.Matches(entry, relative);
                if (included && (string.IsNullOrEmpty(request.Text) || !entry.IsDirectory && !entry.IsLink && entry.Size <= LocalFiles.MaxTextBytes && await Contains(entry)))
                    result.Add(entry);
                if (result.Count >= request.MaxResults)
                {
                    truncated = true;
                    return;
                }

                if (entry.IsDirectory && !entry.IsLink && included)
                    await Scan(entry.Path, relative + "/", depth + 1);
                if (truncated)
                    return;
            }
        }

        async Task<bool> Contains(Entry entry)
        {
            var temp = Path.GetTempFileName();
            try
            {
                await remote.Download(entry.Path, temp, 0, p =>
                {
                    if (p.Bytes > LocalFiles.MaxTextBytes)
                        throw new IOException("검색 파일 크기 초과");
                }, ct);
                var text = TextCodec.Decode(await File.ReadAllBytesAsync(temp, ct), request.Encoding).Content;
                return text.Contains(request.Text!, request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            catch (System.Text.DecoderFallbackException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            finally
            {
                File.Delete(temp);
            }
        }

        await Scan(RemotePaths.Normalize(request.Path), "", 0);
        return new(result.ToArray(), truncated, scanned);
    }

    public static async Task Permissions(IRemoteFileSystem remote, string path, string octal, bool recursive, int? owner, int? group, CancellationToken ct, int depth = 0)
    {
        if (depth > 64)
            throw new IOException("폴더 깊이를 초과했습니다.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(octal, "^[0-7]{3,4}$"))
            throw new ArgumentException("권한은 8진수 3–4자리입니다.");
        var entry = await remote.Stat(path, ct) ?? throw new FileNotFoundException(path);
        if (entry.IsLink)
            throw new IOException("심볼릭 링크의 대상 권한은 변경하지 않습니다.");
        if (recursive && entry.IsDirectory)
            foreach (var child in await remote.List(path, ct))
                if (!child.IsLink)
                    await Permissions(remote, child.Path, octal, true, owner, group, ct, depth + 1);
        if (owner != null || group != null)
            await remote.SetOwner(path, owner, group, ct);
        await remote.Chmod(path, octal, ct);
    }

    public static async Task<object> Properties(IRemoteFileSystem remote, string path, CancellationToken ct)
    {
        var entry = await remote.Stat(path, ct) ?? throw new FileNotFoundException(path);
        long bytes = 0;
        int files = 0, folders = 0;
        async Task Count(Entry current, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > 64 || files + folders > 100000)
                throw new IOException("크기 계산 한도를 초과했습니다.");
            if (current.IsLink)
                return;
            if (current.IsDirectory)
            {
                folders++;
                foreach (var child in await remote.List(current.Path, ct))
                    await Count(child, depth + 1);
            }
            else
            {
                files++;
                bytes += current.Size;
            }
        }

        await Count(entry, 0);
        return new
        {
            entry,
            bytes,
            files,
            folders
        };
    }
}
