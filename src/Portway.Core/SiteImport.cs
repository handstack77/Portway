namespace Portway.Core;

public static class SiteImport
{
    // 비밀 정보가 아닌 설정만 가져옵니다. WinSCP가 난독화한 암호는 의도적으로 해독하지 않습니다.
    public static Site[] FromWinScpIni(string text)
    {
        if (text.Length > 2 * 1024 * 1024)
            throw new ArgumentException("INI 파일이 너무 큽니다.");
        var result = new List<Site>();
        string? name = null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Flush()
        {
            if (name == null || !fields.TryGetValue("HostName", out var host))
                return;
            var protocol = fields.GetValueOrDefault("FSProtocol", "1") switch
            {
                "5" => fields.GetValueOrDefault("FtpSecure", "0") == "0" ? "ftp" : fields.GetValueOrDefault("FtpSecure") == "1" ? "ftps-implicit" : "ftps",
                "6" => fields.GetValueOrDefault("FtpSecure", "0") == "0" ? "webdav" : "webdavs",
                "7" => "s3",
                "0" => "scp",
                _ => "sftp"
            };
            result.Add(new Site { Name = Uri.UnescapeDataString(name), Host = Uri.UnescapeDataString(host), Protocol = protocol, Port = int.TryParse(fields.GetValueOrDefault("PortNumber"), out var port) ? port : 0, Username = Uri.UnescapeDataString(fields.GetValueOrDefault("UserName", "")), RemotePath = Uri.UnescapeDataString(fields.GetValueOrDefault("RemoteDirectory", "/")), LocalPath = Uri.UnescapeDataString(fields.GetValueOrDefault("LocalDirectory", "")), PrivateKeyPath = Uri.UnescapeDataString(fields.GetValueOrDefault("PublicKeyFile", "")) });
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                fields.Clear();
                name = line.StartsWith("[Sessions\\", StringComparison.OrdinalIgnoreCase) ? line[10..^1] : null;
            }
            else if (name != null && line.IndexOf('=') is var pos && pos > 0)
                fields[line[..pos]] = line[(pos + 1)..];
        }

        Flush();
        return result.ToArray();
    }
}
