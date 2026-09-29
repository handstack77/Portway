using System.Text.Json;
using Portway.Core;
using Portway.Core.Automation;

namespace Portway.Tests;

public class AutomationTests
{
    [Fact]
    public async Task ScriptRejectsUnsupportedSettingsAndReturnsFailureEvenInContinueMode()
    {
        using var writer = new StringWriter();
        await using var engine = new ScriptEngine(writer);
        Assert.Equal(1, await engine.Run(["option batch continue", "put -unsupported=1 a b", "echo completed"]));
        using var diagnostic = JsonDocument.Parse(writer.ToString().Split('\n')[0]);
        Assert.Contains("지원하지 않는 옵션입니다", diagnostic.RootElement.GetProperty("error").GetString());
        Assert.Contains("지원하지 않는 옵션입니다", writer.ToString());
        Assert.Contains("completed", writer.ToString());
        Assert.Equal(["put", "C:\\folder name\\a.txt", "/remote path"], ScriptEngine.Tokenize("put \"C:\\folder name\\a.txt\" \"/remote path\""));
    }

    [IntegrationFact]
    public async Task ScriptRunsQuotedPathsTransfersMetadataChecksumAndSyncAgainstSftp()
    {
        var site = await AdvancedProtocolTests.SshSite();
        var root = Path.Combine(Path.GetTempPath(), "portway-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var remote = "/home/portway/files/script-" + Guid.NewGuid().ToString("N");
        await using var inspector = new RemoteFactory().Create(site);
        await inspector.Connect(CancellationToken.None);
        try
        {
            var profile = Path.Combine(root, "site.json");
            File.WriteAllText(profile, JsonSerializer.Serialize(site, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            File.WriteAllText(Path.Combine(root, "한글 file.txt"), "script content");
            Directory.CreateDirectory(Path.Combine(root, "download"));
            using var writer = new StringWriter();
            await using var engine = new ScriptEngine(writer);
            var result = await engine.Run([$"open \"{profile}\"", $"lcd \"{root}\"", $"mkdir {remote}", $"cd {remote}", "put -verify \"한글 file.txt\" .", "chmod 640 *.txt", "checksum sha256 \"한글 file.txt\"", "cp \"한글 file.txt\" copy.txt", "mv copy.txt renamed.txt", "get renamed.txt download", "rm renamed.txt", $"synchronize local \"{Path.Combine(root, "download")}\" {remote}", "call printf '%s' \"quoted command\"", "exit"]);
            Assert.True(result == 0, writer.ToString());
            Assert.Contains("quoted command", writer.ToString());
            Assert.Equal("script content", File.ReadAllText(Path.Combine(root, "download", "한글 file.txt")));
            Assert.Equal("640", (await inspector.Stat(remote + "/한글 file.txt", CancellationToken.None))!.Permissions);
        }
        finally
        {
            if (await inspector.Stat(remote, CancellationToken.None) != null)
                await TransferOperations.DeleteTree(inspector, remote, CancellationToken.None);
            Directory.Delete(root, true);
        }
    }
}
