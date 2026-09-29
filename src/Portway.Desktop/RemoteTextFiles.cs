using Portway.Core;

namespace Portway.Desktop;
// 원격 텍스트 편집의 크기 제한·인코딩·동시 수정 검사·임시 파일 커밋을 모읍니다.
internal static class RemoteTextFiles
{
    static async Task<byte[]> RemoteBytes(IRemoteFileSystem fs, string path, CancellationToken ct)
    {
        var info = await fs.Stat(path, ct) ?? throw new FileNotFoundException(path);
        if (info.IsDirectory || info.IsLink || info.Size > LocalFiles.MaxTextBytes)
            throw new IOException("16 MiB 이하의 일반 텍스트 파일을 선택하세요.");
        var temp = Path.GetTempFileName();
        try
        {
            await fs.Download(path, temp, 0, p =>
            {
                if (p.Bytes > LocalFiles.MaxTextBytes)
                    throw new IOException("파일 크기 제한 초과");
            }, ct);
            return await File.ReadAllBytesAsync(temp, ct);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public static async Task<object> Read(IRemoteFileSystem fs, string path, CancellationToken ct, string? encoding = null)
    {
        var bytes = await RemoteBytes(fs, path, ct);
        return TextCodec.Decode(bytes, encoding);
    }

    public static async Task<object> Write(IRemoteFileSystem fs, FileRequest r, CancellationToken ct)
    {
        var original = await RemoteBytes(fs, r.Path, ct);
        if (LocalFiles.Hash(original) != r.Etag)
            throw new InvalidOperationException("원격 파일이 변경되었습니다. 다시 열어 확인하세요.");
        var detected = TextCodec.Detect(original);
        var bytes = TextCodec.Encode(r.Content ?? "", r.Encoding ?? detected.Name, r.Bom ?? detected.Bom);
        var temp = Path.GetTempFileName();
        var id = Guid.NewGuid().ToString("N");
        var remoteTemp = r.Path + ".portway-edit-" + id;
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            await fs.Upload(temp, remoteTemp, 0, _ =>
            {
            }, ct);
            if (LocalFiles.Hash(await RemoteBytes(fs, r.Path, ct)) != r.Etag)
                throw new InvalidOperationException("저장 중 다른 프로그램이 파일을 수정했습니다. 다시 열어 확인하세요.");
            await TransferOperations.Commit(fs, remoteTemp, r.Path, id, ct);
            return TextCodec.Decode(bytes, r.Encoding ?? detected.Name);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
