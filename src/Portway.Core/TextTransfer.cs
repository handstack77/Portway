namespace Portway.Core;

public static class TextTransfer
{
    public static async Task ConvertNewlines(string source, string destination, bool crlf, CancellationToken ct)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        var buffer = new byte[131072];
        var converted = new byte[262146];
        var previousCr = false;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            var count = 0;
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b == 0)
                    throw new IOException("텍스트 모드에서 바이너리 또는 UTF-16 파일을 변환할 수 없습니다. 바이너리 모드를 선택하세요.");
                if (b == 10 && previousCr)
                {
                    previousCr = false;
                    continue;
                }

                previousCr = b == 13;
                if (b is 10 or 13)
                {
                    if (crlf)
                        converted[count++] = 13;
                    converted[count++] = 10;
                }
                else
                    converted[count++] = b;
            }

            await output.WriteAsync(converted.AsMemory(0, count), ct);
        }
    }
}
