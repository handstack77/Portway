using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Portway.Core;

public static class SiteArchive
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxSites = 1000;
    public record Document(string Format, int Version, Site[] Sites);
    public record ImportResult(Site[] Sites, int Skipped);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        MaxDepth = 16
    };
    // 금고 상태와 관계없이 비밀을 제거하며, 암호화 사용 여부는 보존해 평문 전송을 방지합니다.
    public static Site Metadata(Site site)
    {
        if (site.Proxy == null)
            throw new ArgumentException("사이트의 프록시 설정이 올바르지 않습니다.");
        if (site.Jump?.Jump != null)
            throw new ArgumentException("점프 서버는 한 단계만 지원합니다.");
        var jump = site.Jump == null ? null : Metadata(site.Jump);
        return SiteSecrets.Map(site with { Jump = jump }, _ => null) with
        {
            SavePassword = false,
            HasPassword = false,
            EncryptFiles = site.EncryptFiles || site.EncryptionKey != null
        };
    }

    public static string Export(IEnumerable<Site> sites)
    {
        var items = sites.Select(Metadata).ToArray();
        if (items.Length > MaxSites)
            throw new ArgumentException("사이트는 한 파일에 최대 1,000개까지 내보낼 수 있습니다.");
        var content = JsonSerializer.Serialize(new Document("portway-sites", 1, items), Json);
        CheckSize(content);
        return content;
    }

    public static ImportResult Import(string content)
    {
        CheckSize(content);
        content = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (!content.StartsWith('{'))
        {
            var ini = SiteImport.FromWinScpIni(content);
            if (ini.Length == 0)
                throw new ArgumentException("가져올 사이트가 없습니다. Portway JSON 또는 WinSCP INI 파일을 선택하세요.");
            if (ini.Length > MaxSites)
                throw new ArgumentException("사이트는 한 번에 최대 1,000개까지 가져올 수 있습니다.");
            return new(ini.Where(s => s.Protocol != "s3").ToArray(), ini.Count(s => s.Protocol == "s3"));
        }

        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(content, Json) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new ArgumentException("Portway 사이트 JSON 파일의 형식이 올바르지 않습니다.");
        }

        if (document.Format != "portway-sites" || document.Version != 1 || document.Sites == null)
            throw new ArgumentException("지원하지 않는 사이트 파일 형식 또는 버전입니다.");
        if (document.Sites.Length == 0)
            throw new ArgumentException("가져올 사이트가 없습니다.");
        if (document.Sites.Length > MaxSites)
            throw new ArgumentException("사이트는 한 번에 최대 1,000개까지 가져올 수 있습니다.");
        var sites = document.Sites.Select(site => site == null ? throw new ArgumentException("사이트 항목이 올바르지 않습니다.") : Metadata(site)).ToArray();
        return new(sites, 0);
    }

    static void CheckSize(string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes)
            throw new ArgumentException("사이트 파일은 2 MiB 이하여야 합니다.");
    }
}
