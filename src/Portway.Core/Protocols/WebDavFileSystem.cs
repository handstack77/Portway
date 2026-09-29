using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Portway.Core.Protocols;

public sealed class WebDavFileSystem : IRemoteFileSystem
{
    readonly HttpClient http;
    readonly Uri root;
    public WebDavFileSystem(Site site)
    {
        root = new Uri($"{(site.Protocol == "webdavs" ? "https" : "http")}://{site.Host}:{site.EffectivePort}/");
        http = new HttpClient(TlsOptions.HttpHandler(site))
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    public Capabilities Capabilities => new(false, false, false, true);

    Uri Url(string path) => new(root, string.Join('/', RemotePaths.Normalize(path).Split('/').Select(Uri.EscapeDataString)));
    public async Task Connect(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, root);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            response.EnsureSuccessStatusCode();
    }

    async Task<Entry[]> Propfind(string path, string depth, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), Url(path));
        request.Headers.Add("Depth", depth);
        request.Content = new StringContent("<d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            if (depth == "1")
                throw new DirectoryNotFoundException("원격 폴더를 찾을 수 없습니다.");
            return [];
        }

        response.EnsureSuccessStatusCode();
        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        XNamespace d = "DAV:";
        return xml.Descendants(d + "response").Select(r =>
        {
            var href = r.Element(d + "href")?.Value ?? throw new IOException("잘못된 WebDAV 응답입니다.");
            var uri = new Uri(root, href);
            if (uri.Authority != root.Authority)
                throw new IOException("WebDAV 응답의 호스트가 다릅니다.");
            var p = RemotePaths.Normalize(Uri.UnescapeDataString(uri.AbsolutePath));
            var prop = r.Elements(d + "propstat").FirstOrDefault(x => x.Element(d + "status")?.Value.Contains(" 200 ") == true)?.Element(d + "prop");
            return new Entry(RemotePaths.Name(p), p, prop?.Element(d + "resourcetype")?.Element(d + "collection") != null, long.TryParse(prop?.Element(d + "getcontentlength")?.Value, out var size) ? size : 0, DateTimeOffset.TryParse(prop?.Element(d + "getlastmodified")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : DateTimeOffset.UnixEpoch);
        }).ToArray();
    }

    public async Task<Entry[]> List(string path, CancellationToken ct) => (await Propfind(path, "1", ct)).Where(f => f.Path != RemotePaths.Normalize(path)).ToArray();
    public async Task<Entry?> Stat(string path, CancellationToken ct) => (await Propfind(path, "0", ct)).FirstOrDefault();
    public async Task CreateDirectory(string path, CancellationToken ct)
    {
        using var r = await http.SendAsync(new HttpRequestMessage(new HttpMethod("MKCOL"), Url(path)), ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task Delete(string path, bool directory, CancellationToken ct)
    {
        using var r = await http.DeleteAsync(Url(path), ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task Move(string source, string destination, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod("MOVE"), Url(source));
        request.Headers.Add("Destination", Url(destination).AbsoluteUri);
        request.Headers.Add("Overwrite", "F");
        using var r = await http.SendAsync(request, ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task Copy(string source, string destination, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod("COPY"), Url(source));
        request.Headers.Add("Destination", Url(destination).AbsoluteUri);
        request.Headers.Add("Overwrite", "F");
        request.Headers.Add("Depth", "infinity");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task Download(string remote, string local, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(remote));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(local);
        await StreamCopy.Copy(input, output, 0, response.Content.Headers.ContentLength ?? 0, progress, ct);
    }

    public async Task Upload(string local, string remote, long offset, Action<TransferProgress> progress, CancellationToken ct)
    {
        await using var file = File.OpenRead(local);
        using var content = new ProgressHttpContent(file, progress);
        using var response = await http.PutAsync(Url(remote), content, ct);
        response.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync()
    {
        http.Dispose();
        return ValueTask.CompletedTask;
    }

    sealed class ProgressHttpContent(Stream stream, Action<TransferProgress> progress) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = stream.Length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => StreamCopy.Copy(stream, target, 0, stream.Length, progress, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context, CancellationToken ct) => StreamCopy.Copy(stream, target, 0, stream.Length, progress, ct);
    }
}
