namespace Portway.Core;

/// <summary>UI에 의존하지 않는 재귀 전송·충돌·부분 파일·커밋과 이동 처리를 제공합니다.</summary>
public static class TransferOperations
{
    public static async Task EnsureDirectory(IRemoteFileSystem remote, string path, CancellationToken ct)
    {
        path = RemotePaths.Normalize(path);
        if (path == "/")
            return;
        var info = await remote.Stat(path, ct);
        if (info != null)
        {
            if (!info.IsDirectory || info.IsLink)
                throw new IOException("대상이 폴더가 아니거나 심볼릭 링크입니다: " + path);
            return;
        }

        await EnsureDirectory(remote, RemotePaths.Parent(path)!, ct);
        await remote.CreateDirectory(path, ct);
    }

    public static async Task DeleteTree(IRemoteFileSystem remote, string path, CancellationToken ct, int depth = 0)
    {
        if (depth > 64 || RemotePaths.Normalize(path) == "/")
            throw new IOException("루트 삭제 또는 과도한 폴더 깊이는 허용하지 않습니다.");
        var info = await remote.Stat(path, ct) ?? throw new FileNotFoundException(path);
        if (info.IsDirectory && !info.IsLink)
            foreach (var child in await remote.List(path, ct))
                await DeleteTree(remote, child.Path, ct, depth + 1);
        await remote.Delete(path, info.IsDirectory && !info.IsLink, ct);
    }

    public static async Task Commit(IRemoteFileSystem remote, string temporary, string destination, string operationId, CancellationToken ct)
    {
        var old = await remote.Stat(destination, ct);
        if (old == null)
        {
            await remote.Move(temporary, destination, ct);
            return;
        }

        if (old.IsDirectory || old.IsLink)
            throw new IOException("폴더나 심볼릭 링크를 덮어쓸 수 없습니다.");
        if (remote.Capabilities.Permissions && System.Text.RegularExpressions.Regex.IsMatch(old.Permissions, "^[0-7]{3,4}$"))
            await remote.Chmod(temporary, old.Permissions, ct);
        var backup = destination + ".portway-backup-" + operationId;
        await remote.Move(destination, backup, ct);
        try
        {
            await remote.Move(temporary, destination, ct);
        }
        catch
        {
            await remote.Move(backup, destination, CancellationToken.None);
            throw;
        }

        await remote.Delete(backup, false, ct);
    }

    public static async Task Transfer(IRemoteFileSystem remote, string direction, string source, string destinationDirectory, string conflict, string operationId, Action<string, TransferProgress> progress, CancellationToken ct, int depth = 0, Dictionary<string, Entry>? snapshots = null, HashSet<string>? completed = null, TransferOptions? options = null, string prefix = "")
    {
        ct.ThrowIfCancellationRequested();
        if (depth > 64)
            throw new IOException("폴더 깊이 제한을 초과했습니다.");
        var upload = direction == "upload";
        options ??= new();
        if (depth == 0)
            options.Validate();
        var entry = upload ? LocalFiles.Stat(source) : await remote.Stat(source, ct) ?? throw new FileNotFoundException(source);
        if (completed?.Contains(source) == true)
            return;
        var relative = prefix + entry.Name;
        var mask = new FileMask(options.FileMask);
        if (!mask.Matches(entry, relative) || options.ExcludeHidden && (entry.Name.StartsWith('.') || (upload || remote is LocalTransferFileSystem) && File.GetAttributes(source).HasFlag(FileAttributes.Hidden)))
            return;
        if (entry.IsLink)
            throw new IOException("심볼릭 링크는 자동으로 따라가지 않습니다: " + source);
        var targetName = options.TargetName(entry.Name);
        var target = upload ? RemotePaths.Join(destinationDirectory, targetName) : RemotePaths.SafeLocalChild(destinationDirectory, targetName);
        if (entry.IsDirectory)
        {
            var children = upload ? LocalFiles.List(source).Entries : await remote.List(source, ct);
            children = children.Where(child => mask.Matches(child, relative + "/" + child.Name) && (!options.ExcludeHidden || !child.Name.StartsWith('.') && (!(upload || remote is LocalTransferFileSystem) || !File.GetAttributes(child.Path).HasFlag(FileAttributes.Hidden)))).ToArray();
            if (options.ExcludeEmptyDirectories && !await HasFiles(remote, upload, source, relative + "/", options, ct, depth))
                return;
            var comparer = !upload && (OperatingSystem.IsWindows() || remote is LocalTransferFileSystem && OperatingSystem.IsMacOS()) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            if (children.GroupBy(c => options.TargetName(c.Name), comparer).Any(g => g.Count() > 1))
                throw new IOException("이름 변환 후 대상 파일 이름이 충돌합니다: " + source);
            if (upload)
                await EnsureDirectory(remote, target, ct);
            else
            {
                if (Directory.Exists(target) && new DirectoryInfo(target).LinkTarget != null)
                    throw new IOException("대상 폴더가 심볼릭 링크입니다.");
                Directory.CreateDirectory(target);
            }

            foreach (var child in children)
                await Transfer(remote, direction, child.Path, target, conflict, operationId, progress, ct, depth + 1, snapshots, completed, options, relative + "/");
            if (options.PreserveTimestamp)
            {
                if (upload && remote.Capabilities.Timestamps)
                    await remote.SetModified(target, entry.Modified, ct);
                else if (!upload)
                    Directory.SetLastWriteTimeUtc(target, entry.Modified.UtcDateTime);
            }

            if (options.RemoveSource && (upload ? !Directory.EnumerateFileSystemEntries(source).Any() : (await remote.List(source, ct)).Length == 0))
            {
                if (upload)
                    Directory.Delete(source);
                else
                    await remote.Delete(source, true, ct);
            }

            return;
        }

        var destination = upload ? await remote.Stat(target, ct) : File.Exists(target) || Directory.Exists(target) ? LocalFiles.Stat(target) : null;
        var exists = destination != null;
        if (exists && conflict == "rename")
        {
            var original = target;
            var counter = 1;
            do
            {
                if (counter > 10000)
                    throw new IOException("이름이 같은 파일이 너무 많습니다.");
                target = original + " (" + counter++ + ")";
            }
            while (upload ? await remote.Stat(target, ct) != null : File.Exists(target) || Directory.Exists(target));
            exists = false;
            destination = null;
        }

        if (snapshots != null)
        {
            if (snapshots.TryGetValue(source, out var snapshot) && (snapshot.Size != entry.Size || snapshot.Modified != entry.Modified))
                throw new IOException("원본이 변경되어 이어받을 수 없습니다. 새 전송을 시작하세요: " + source);
            snapshots[source] = entry;
        }

        if (exists && conflict == "skip")
        {
            progress(source, new(entry.Size, entry.Size));
            return;
        }

        if (destination != null && (destination.IsDirectory || destination.IsLink))
            throw new IOException("폴더나 심볼릭 링크를 덮어쓸 수 없습니다.");
        if (destination != null && (conflict == "newer" || options.NewerOnly) && destination.Modified >= entry.Modified)
        {
            progress(source, new(entry.Size, entry.Size));
            return;
        }

        if (upload)
            await EnsureDirectory(remote, destinationDirectory, ct);
        else
            Directory.CreateDirectory(destinationDirectory);
        var part = target + ".portway-part-" + operationId;
        var partial = upload ? await remote.Stat(part, ct) : File.Exists(part) || Directory.Exists(part) ? LocalFiles.Stat(part) : null;
        if (partial is { IsDirectory: true } or { IsLink: true })
            throw new IOException("임시 전송 파일이 폴더나 링크입니다.");
        var text = options.Mode == "text" || options.Mode == "automatic" && new FileMask(options.TextMask).Matches(entry, relative);
        var converted = text ? Path.GetTempFileName() : null;
        var payload = source;
        try
        {
            if (upload && text)
            {
                await TextTransfer.ConvertNewlines(source, converted!, options.RemoteNewline == "crlf", ct);
                payload = converted!;
            }

            var payloadSize = upload ? new FileInfo(payload).Length : entry.Size;
            long offset = 0;
            if (remote.Capabilities.Resume && options.Resume)
                offset = partial?.Size ?? 0;
            if (offset > payloadSize)
                offset = 0;
            if (upload)
            {
                await remote.Upload(payload, part, offset, p => progress(source, p), ct);
                var after = LocalFiles.Stat(source);
                if (after.Size != entry.Size || after.Modified != entry.Modified)
                    throw new IOException("전송 중 원본이 변경되었습니다: " + source);
                if ((await remote.Stat(part, ct))?.Size != payloadSize)
                    throw new IOException("업로드 파일 크기가 일치하지 않습니다.");
                if (options.VerifyChecksum && await FileChecksums.Local(payload, "sha256", ct) != await FileChecksums.Remote(remote, part, "sha256", ct))
                    throw new IOException("업로드 SHA256 검증에 실패했습니다.");
                if (options.PreserveTimestamp && remote.Capabilities.Timestamps)
                    await remote.SetModified(part, entry.Modified, ct);
                await Commit(remote, part, target, operationId, ct);
                if (options.Permissions != null)
                    await remote.Chmod(target, options.Permissions, ct);
            }
            else
            {
                await remote.Download(source, part, offset, p => progress(source, p), ct);
                var after = await remote.Stat(source, ct);
                if (after == null || after.Size != entry.Size || after.Modified != entry.Modified)
                    throw new IOException("전송 중 원본이 변경되었습니다: " + source);
                if (new FileInfo(part).Length != entry.Size)
                    throw new IOException("다운로드 파일 크기가 일치하지 않습니다.");
                if (options.VerifyChecksum && await FileChecksums.Local(part, "sha256", ct) != await FileChecksums.Remote(remote, source, "sha256", ct))
                    throw new IOException("다운로드 SHA256 검증에 실패했습니다.");
                if (File.Exists(target) && new FileInfo(target).LinkTarget != null)
                    throw new IOException("심볼릭 링크를 덮어쓸 수 없습니다.");
                if (text)
                {
                    await TextTransfer.ConvertNewlines(part, converted!, OperatingSystem.IsWindows(), ct);
                    var normalized = part + ".normalized";
                    File.Copy(converted!, normalized, true);
                    File.Move(normalized, part, true);
                }

                File.Move(part, target, conflict != "skip");
                if (options.PreserveTimestamp)
                    File.SetLastWriteTimeUtc(target, entry.Modified.UtcDateTime);
                if (options.PreserveReadOnly && entry.Permissions.Length >= 3 && (entry.Permissions[^3] - '0' & 2) == 0)
                    File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly);
            }

            if (options.RemoveSource)
            {
                if (upload)
                    File.Delete(source);
                else
                    await remote.Delete(source, false, ct);
            }

            progress(source, new(entry.Size, entry.Size));
            completed?.Add(source);
        }
        finally
        {
            if (converted != null)
                File.Delete(converted);
        }
    }

    static async Task<bool> HasFiles(IRemoteFileSystem remote, bool upload, string source, string prefix, TransferOptions options, CancellationToken ct, int depth)
    {
        if (depth > 64)
            throw new IOException("폴더 깊이 제한을 초과했습니다.");
        var mask = new FileMask(options.FileMask);
        foreach (var child in upload ? LocalFiles.List(source).Entries : await remote.List(source, ct))
        {
            if (child.IsLink || !mask.Matches(child, prefix + child.Name) || options.ExcludeHidden && child.Name.StartsWith('.'))
                continue;
            if (!child.IsDirectory || await HasFiles(remote, upload, child.Path, prefix + child.Name + "/", options, ct, depth + 1))
                return true;
        }

        return false;
    }
}
