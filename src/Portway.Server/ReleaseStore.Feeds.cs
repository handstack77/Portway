using Velopack;

namespace Portway.Server;

public sealed partial class ReleaseStore
{
    // ZIP은 해당 버전만 담고, 서버 피드는 이전 변경분 연결을 보존합니다.
    static Feed MergeFeeds(Feed existing, Feed incoming)
    {
        var assets = new Dictionary<(SemanticVersion Version, string Type), Asset>();
        var files = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { existing, incoming })
        {
            var versions = new HashSet<(SemanticVersion, string)>();
            foreach (var asset in source.Assets)
            {
                if (asset == null || asset.Type is not ("Full" or "Delta") || asset.PackageId != "Portway")
                    throw new IOException("Portway Full/Delta 패키지만 피드에 등록할 수 있습니다.");
                ValidateFile(asset.FileName);
                if (!asset.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || !SemanticVersion.TryParse(asset.Version, out var version))
                    throw new IOException("패키지 파일 또는 SemVer 버전이 잘못되었습니다.");
                var key = (version, asset.Type);
                if (!versions.Add(key))
                    throw new IOException("피드에 같은 버전·형식의 패키지가 중복되었습니다.");
                if (assets.TryGetValue(key, out var known) && !SamePackage(known, asset))
                    throw new IOException("이미 게시한 버전의 패키지 정보를 변경할 수 없습니다.");
                if (files.TryGetValue(asset.FileName, out var file) && !SamePackage(file, asset))
                    throw new IOException("다른 패키지에 같은 파일 이름을 사용할 수 없습니다.");
                assets.TryAdd(key, asset);
                files.TryAdd(asset.FileName, asset);
            }
        }
        var latest = existing.Assets.Where(a => a.Type == "Full").Select(a => SemanticVersion.Parse(a.Version)).OrderDescending().FirstOrDefault();
        foreach (var asset in incoming.Assets.Where(a => a.Type == "Delta" && a.BaseVersion != null))
        {
            if (!SemanticVersion.TryParse(asset.BaseVersion, out var basis) || basis >= SemanticVersion.Parse(asset.Version) || !assets.ContainsKey((basis, "Full")))
                throw new IOException("변경분의 기준 Full 버전을 먼저 게시해야 합니다.");
            if (latest != null && SemanticVersion.Parse(asset.Version) > latest && basis != latest)
                throw new IOException("변경분은 현재 채널의 최신 Full 버전을 기준으로 생성하세요.");
        }
        foreach (var asset in assets.Values.Where(a => a.Type == "Delta"))
            if (!assets.ContainsKey((SemanticVersion.Parse(asset.Version), "Full")))
                throw new IOException("변경분과 같은 버전의 Full 패키지가 필요합니다.");
        return new Feed(assets.Values.OrderByDescending(a => SemanticVersion.Parse(a.Version)).ThenBy(a => a.Type == "Full" ? 0 : 1).ToArray());
    }

    static bool SamePackage(Asset left, Asset right) =>
        left.PackageId == right.PackageId && SemanticVersion.Parse(left.Version) == SemanticVersion.Parse(right.Version) &&
        left.Type == right.Type && left.FileName == right.FileName && left.Size == right.Size &&
        string.Equals(left.SHA1, right.SHA1, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.SHA256, right.SHA256, StringComparison.OrdinalIgnoreCase) &&
        left.BaseVersion == right.BaseVersion;
}
