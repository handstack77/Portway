using System.Security.Cryptography;
using System.Text;

namespace Portway.Core;
// 공개된 WinSCP aesctr.enc 파일 형식을 독립적으로 구현합니다.
// 호환 형식은 AES-256 CTR이며 임의의 128비트 카운터를 사용하고 인증 태그는 없습니다.
public static class WinScpEncryption
{
    public const string Extension = ".aesctr.enc";
    static readonly byte[] Header = "aesctr.........."u8.ToArray();
    public static byte[] ParseKey(string key)
    {
        var bytes = Convert.FromHexString(key);
        if (bytes.Length != 32)
            throw new ArgumentException("암호화 키는 64자리 16진수여야 합니다.");
        return bytes;
    }

    public static string EncryptName(string name, byte[] key)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var data = new UTF8Encoding(false, true).GetBytes(name);
        Transform(data, key, salt);
        var encoded = Convert.ToBase64String([.. salt, .. data]).TrimEnd('=').Replace('/', '_') + Extension;
        if (encoded.Length > 255)
            throw new IOException("암호화된 파일 이름이 서버의 255바이트 제한을 초과합니다.");
        return encoded;
    }

    public static string DecryptName(string name, byte[] key)
    {
        if (!name.EndsWith(Extension, StringComparison.Ordinal))
            return name;
        var encoded = name[..^Extension.Length].Replace('_', '/');
        encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length <= 16)
            throw new IOException("암호화 파일 이름 형식이 올바르지 않습니다.");
        var data = bytes[16..];
        Transform(data, key, bytes[..16]);
        var plain = new UTF8Encoding(false, true).GetString(data);
        if (plain is "" or "." or ".." || plain.IndexOfAny(['/', '\0']) >= 0)
            throw new IOException("암호화 키 또는 파일 이름이 올바르지 않습니다.");
        return plain;
    }

    public static void Transform(Span<byte> data, byte[] key, byte[] initialCounter)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var cipher = aes.CreateEncryptor();
        var counter = (byte[])initialCounter.Clone();
        var mask = new byte[16];
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            cipher.TransformBlock(counter, 0, 16, mask, 0);
            for (var i = 0; i < Math.Min(16, data.Length - offset); i++)
                data[offset + i] ^= mask[i];
            Increment(counter);
        }

        CryptographicOperations.ZeroMemory(mask);
        CryptographicOperations.ZeroMemory(counter);
    }

    static void Increment(byte[] counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            counter[i]++;
            if (counter[i] != 0)
                break;
        }
    }

    public static async Task File(string source, string destination, byte[] key, bool decrypt, CancellationToken ct)
    {
        await using var input = System.IO.File.OpenRead(source);
        await using var output = System.IO.File.Create(destination);
        if (input.Length == 0)
            return;
        var counter = new byte[16];
        if (decrypt)
        {
            var header = new byte[16];
            await input.ReadExactlyAsync(header, ct);
            if (!header.SequenceEqual(Header))
                throw new IOException("잘못된 암호화 파일 헤더입니다.");
            await input.ReadExactlyAsync(counter, ct);
        }
        else
        {
            RandomNumberGenerator.Fill(counter);
            await output.WriteAsync(Header, ct);
            await output.WriteAsync(counter, ct);
        }

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var cipher = aes.CreateEncryptor();
        var buffer = new byte[131072];
        var mask = new byte[16];
        var maskIndex = 16;
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    if (maskIndex == 16)
                    {
                        cipher.TransformBlock(counter, 0, 16, mask, 0);
                        Increment(counter);
                        maskIndex = 0;
                    }

                    buffer[i] ^= mask[maskIndex++];
                }

                await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(mask);
            CryptographicOperations.ZeroMemory(counter);
        }
    }
}
