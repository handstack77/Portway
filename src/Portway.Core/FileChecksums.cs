using System.Security.Cryptography;

namespace Portway.Core;

public static class FileChecksums
{
    public static async Task<string> Local(string path, string algorithm, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(algorithm.ToLowerInvariant() switch
        {
            "md5" => HashAlgorithmName.MD5,
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException("MD5, SHA1, SHA256, SHA512를 지원합니다.")
        });
        await using var stream = File.OpenRead(path);
        var buffer = new byte[131072];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static async Task<string> Remote(IRemoteFileSystem remote, string path, string algorithm, CancellationToken ct)
    {
        var temp = Path.GetTempFileName();
        try
        {
            await remote.Download(path, temp, 0, _ =>
            {
            }, ct);
            return await Local(temp, algorithm, ct);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
