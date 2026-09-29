using System.Text;

namespace Portway.Core;

public record TextDocument(string Content, string Etag, string Encoding, bool Bom);
public static class TextCodec
{
    static TextCodec() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static (string Name, bool Bom) Detect(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }))
            return ("utf-32le", true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
            return ("utf-32be", true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            return ("utf-8", true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
            return ("utf-16le", true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
            return ("utf-16be", true);
        return ("utf-8", false);
    }

    public static Encoding GetEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" => new UTF8Encoding(false, true),
        "utf-16" or "utf-16le" => new UnicodeEncoding(false, false, true),
        "utf-16be" => new UnicodeEncoding(true, false, true),
        "utf-32" or "utf-32le" => new UTF32Encoding(false, false, true),
        "utf-32be" => new UTF32Encoding(true, false, true),
        _ when name.StartsWith("cp", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[2..], out var codepage) => System.Text.Encoding.GetEncoding(codepage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        _ => System.Text.Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    };
    public static TextDocument Decode(byte[] bytes, string? name = null)
    {
        if (bytes.Length > LocalFiles.MaxTextBytes)
            throw new IOException("내장 편집기는 16 MiB 이하 파일을 지원합니다. 큰 파일은 외부 편집기를 사용하세요.");
        var detected = "utf-8";
        var offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }))
        {
            detected = "utf-32le";
            offset = 4;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
        {
            detected = "utf-32be";
            offset = 4;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            detected = "utf-8";
            offset = 3;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            detected = "utf-16le";
            offset = 2;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            detected = "utf-16be";
            offset = 2;
        }

        var selected = string.IsNullOrEmpty(name) || name == "auto" ? detected : name;
        if (offset > 0 && selected != detected && selected != "utf-16" && selected != "utf-32")
            offset = 0;
        var content = GetEncoding(selected).GetString(bytes, offset, bytes.Length - offset);
        if (content.Contains('\0'))
            throw new InvalidOperationException("바이너리 파일은 외부 편집기를 사용하세요.");
        return new(content, LocalFiles.Hash(bytes), selected, offset > 0);
    }

    public static byte[] Encode(string content, string name, bool bom)
    {
        if (content.Contains('\0'))
            throw new ArgumentException("텍스트에 NUL 문자를 포함할 수 없습니다.");
        var bytes = GetEncoding(name).GetBytes(content);
        var preamble = !bom ? [] : name.ToLowerInvariant() switch
        {
            "utf-8" => new byte[]
            {
                0xef,
                0xbb,
                0xbf
            },
            "utf-16" or "utf-16le" => [0xff, 0xfe],
            "utf-16be" => [0xfe, 0xff],
            "utf-32" or "utf-32le" => [0xff, 0xfe, 0, 0],
            "utf-32be" => [0, 0, 0xfe, 0xff],
            _ => []
        };
        if (bytes.Length + preamble.Length > LocalFiles.MaxTextBytes)
            throw new IOException("내장 편집기 크기 제한을 초과했습니다.");
        return [.. preamble, .. bytes];
    }
}
