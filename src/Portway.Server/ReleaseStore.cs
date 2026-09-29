using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Portway.Server;

public sealed partial class ReleaseStore
{
    readonly string root;
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    public sealed record Asset(string PackageId, string Version, string Type, string FileName, string SHA1, string? SHA256, long Size)
    {
        public string? BaseVersion { get; init; }
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? Metadata { get; init; }
    }
    public sealed record Feed(Asset[] Assets);
    public ReleaseStore(IConfiguration config)
    {
        root = Path.GetFullPath(config["Distribution:DataPath"] ?? Path.Combine(AppContext.BaseDirectory, "data"));
        Directory.CreateDirectory(root);
    }

    [GeneratedRegex("^(win|osx|linux)-(x64|arm64)-(stable|beta)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelPattern();
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._+-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex FilePattern();
    public static void ValidateChannel(string channel)
    {
        if (!ChannelPattern().IsMatch(channel))
            throw new ArgumentException("채널은 win-x64-stable / osx-arm64-stable / linux-x64-beta 형식이어야 합니다.");
    }

    public static void ValidateFile(string file)
    {
        if (!FilePattern().IsMatch(file) || file.Contains("..") || file.StartsWith('.'))
            throw new ArgumentException("안전하지 않은 파일 이름입니다.");
        if (!new[]
        {
            ".json",
            ".nupkg",
            ".exe",
            ".msi",
            ".zip",
            ".pkg",
            ".dmg",
            ".AppImage"
        }.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase)) && !file.StartsWith("RELEASES"))
            throw new ArgumentException("허용되지 않은 배포 파일입니다.");
    }

    public string? Resolve(string channel, string file)
    {
        ValidateChannel(channel);
        ValidateFile(file);
        var dir = Path.Combine(root, channel);
        var path = Path.Combine(dir, file);
        return File.Exists(path) ? path : null;
    }

    public object[] List() => Directory.EnumerateDirectories(root).Where(d => ChannelPattern().IsMatch(Path.GetFileName(d))).Select(d =>
    {
        var channel = Path.GetFileName(d);
        var feedPath = Path.Combine(d, $"releases.{channel}.json");
        var feed = File.Exists(feedPath) ? JsonSerializer.Deserialize<Feed>(File.ReadAllText(feedPath), Json) : null;
        return (object)new
        {
            channel,
            feed = $"/releases/{channel}/",
            assets = feed?.Assets ?? [],
            downloads = Directory.EnumerateFiles(d).Where(f => new[] { ".exe", ".pkg", ".AppImage", ".zip", ".msi" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Select(f => new { name = Path.GetFileName(f), size = new FileInfo(f).Length, url = $"/releases/{channel}/{Path.GetFileName(f)}" }).ToArray()
        };
    }).ToArray();
    public async Task<object> Publish(string channel, Stream body, CancellationToken ct)
    {
        ValidateChannel(channel);
        var gate = gates.GetOrAdd(channel, _ => new(1));
        await gate.WaitAsync(ct);
        var stage = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var archivePath = Path.Combine(stage, "upload.bin");
            await using (var output = File.Create(archivePath))
                await BoundedCopy(body, output, 2L * 1024 * 1024 * 1024, ct);
            var incoming = new List<string>();
            long total = 0;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count is 0 or > 256)
                    throw new IOException("ZIP에는 1–256개 파일이 필요합니다.");
                foreach (var entry in archive.Entries)
                {
                    ValidateFile(entry.FullName);
                    if (incoming.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase))
                        throw new IOException("ZIP에 중복 파일 이름이 있습니다.");
                    total += entry.Length;
                    if (total > 4L * 1024 * 1024 * 1024)
                        throw new IOException("압축 해제 크기 제한 4 GB를 초과했습니다.");
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new IOException("ZIP 심볼릭 링크는 허용하지 않습니다.");
                    await using var source = entry.Open();
                    await using var target = File.Create(Path.Combine(stage, entry.FullName));
                    await BoundedCopy(source, target, entry.Length, ct);
                    incoming.Add(entry.FullName);
                }
            }

            var feedName = $"releases.{channel}.json";
            if (!incoming.Contains(feedName))
                throw new IOException(feedName + " 피드가 필요합니다.");
            if (new FileInfo(Path.Combine(stage, feedName)).Length > 8 * 1024 * 1024)
                throw new IOException("피드가 너무 큽니다.");
            var feed = JsonSerializer.Deserialize<Feed>(await File.ReadAllTextAsync(Path.Combine(stage, feedName), ct), Json) ?? throw new IOException("잘못된 피드입니다.");
            if (feed.Assets == null || feed.Assets.Length == 0 || !feed.Assets.Any(a => a?.Type == "Full"))
                throw new IOException("Full 패키지가 필요합니다.");
            var targetDir = Path.Combine(root, channel);
            Directory.CreateDirectory(targetDir);
            var existingPath = Path.Combine(targetDir, feedName);
            var existing = File.Exists(existingPath)
                ? JsonSerializer.Deserialize<Feed>(await File.ReadAllTextAsync(existingPath, ct), Json) ?? throw new IOException("저장된 피드가 잘못되었습니다.")
                : new Feed([]);
            var merged = MergeFeeds(existing, feed);
            var legacyName = $"RELEASES-{channel}";
            await File.WriteAllLinesAsync(Path.Combine(stage, legacyName), merged.Assets.Select(a => $"{a.SHA1} {a.FileName} {a.Size}"), ct);
            if (!incoming.Contains(legacyName))
                incoming.Add(legacyName);
            foreach (var asset in feed.Assets)
            {
                ValidateFile(asset.FileName);
                if (!asset.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || asset.PackageId != "Portway")
                    throw new IOException("Portway nupkg만 배포할 수 있습니다.");
                var file = incoming.Contains(asset.FileName) ? Path.Combine(stage, asset.FileName) : Path.Combine(targetDir, asset.FileName);
                if (!File.Exists(file) || new FileInfo(file).Length != asset.Size)
                    throw new IOException("패키지 크기 또는 존재 확인 실패: " + asset.FileName);
                await using var stream = File.OpenRead(file);
                var sha1 = Convert.ToHexString(await SHA1.HashDataAsync(stream, ct));
                if (!string.Equals(sha1, asset.SHA1, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("SHA1 검증 실패: " + asset.FileName);
                if (!string.IsNullOrEmpty(asset.SHA256))
                {
                    stream.Position = 0;
                    var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                    if (!string.Equals(sha256, asset.SHA256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("SHA256 검증 실패: " + asset.FileName);
                }
            }

            var knownPackages = merged.Assets.Select(a => a.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (incoming.Any(name => name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !knownPackages.Contains(name)))
                throw new IOException("피드에 없는 패키지는 게시할 수 없습니다.");

            // 변경 불가능한 패키지 파일을 먼저 배치하고 피드를 원자적으로 교체할 때 릴리스를 확정합니다.
            foreach (var name in incoming.Where(n => n != feedName))
            {
                var source = Path.Combine(stage, name);
                var target = Path.Combine(targetDir, name);
                if (name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                {
                    await using var a = File.OpenRead(source);
                    await using var b = File.OpenRead(target);
                    var ah = await SHA256.HashDataAsync(a, ct);
                    var bh = await SHA256.HashDataAsync(b, ct);
                    if (!ah.SequenceEqual(bh))
                        throw new IOException("같은 버전의 패키지 내용을 변경할 수 없습니다.");
                }
            }

            var latestExisting = existing.Assets.Where(a => a.Type == "Full").Select(a => Velopack.SemanticVersion.Parse(a.Version)).OrderDescending().FirstOrDefault();
            var latestIncoming = feed.Assets.Where(a => a.Type == "Full").Select(a => Velopack.SemanticVersion.Parse(a.Version)).Max();
            foreach (var name in incoming.Where(n => n != feedName))
            {
                // 이전 버전을 나중에 게시해도 최신 설치 파일과 자산 목록을 되돌리지 않습니다.
                var package = name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
                if (!package && name != legacyName && latestExisting != null && latestIncoming < latestExisting)
                    continue;
                var destination = Path.Combine(targetDir, name);
                if (package && File.Exists(destination))
                    continue;
                File.Move(Path.Combine(stage, name), destination, true);
            }
            await File.WriteAllTextAsync(Path.Combine(stage, feedName), JsonSerializer.Serialize(merged, Json), ct);
            File.Move(Path.Combine(stage, feedName), Path.Combine(targetDir, feedName), true);
            return new
            {
                channel,
                assets = merged.Assets.Length,
                feed = $"/releases/{channel}/",
                publishedAt = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            Directory.Delete(stage, true);
            gate.Release();
        }
    }

    static async Task BoundedCopy(Stream input, Stream output, long max, CancellationToken ct)
    {
        var bytes = new byte[131072];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(bytes, ct)) > 0)
        {
            total += read;
            if (total > max)
                throw new IOException("업로드 크기 제한을 초과했습니다.");
            await output.WriteAsync(bytes.AsMemory(0, read), ct);
        }
    }
}
