using System.Security.Cryptography;
using Portway.Core;
using Portway.Core.Protocols;

namespace Portway.Tests;

public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("PORTWAY_INTEGRATION") != "1")
            Skip = "Run tests/infrastructure compose and set PORTWAY_INTEGRATION=1.";
    }
}

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PORTWAY_INTEGRATION") != "1")
            Skip = "Protocol fixtures are opt-in.";
    }
}

public class ProtocolTests
{
    static async Task<Site> SiteFor(string protocol)
    {
        var site = new Site
        {
            Name = "test",
            Protocol = protocol,
            Host = protocol == "s3" ? "http://127.0.0.1:25000" : "127.0.0.1",
            Port = protocol switch
            {
                "ftp" => 22121,
                "webdav" => 28081,
                "s3" => 25000,
                _ => 22220
            },
            Username = protocol == "s3" ? "test" : "portway",
            Password = protocol == "s3" ? "test" : "portway-test-only",
            Bucket = "portway-tests",
            RemotePath = protocol is "sftp" or "scp" ? "/home/portway/files" : "/"
        };
        return protocol is "sftp" or "scp" ? site with
        {
            Fingerprint = await SshConnection.Scan(site, CancellationToken.None)
        }

        : site;
    }

    [IntegrationTheory]
    [InlineData("sftp")]
    [InlineData("scp")]
    [InlineData("ftp")]
    [InlineData("webdav")]
    [InlineData("s3")]
    public async Task RealProtocolRoundTripRenameDeleteAndSync(string protocol)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var site = await SiteFor(protocol);
        await using var remote = new RemoteFactory().Create(site);
        await remote.Connect(ct);
        var remoteDir = RemotePaths.Join(site.RemotePath, "test-" + Guid.NewGuid().ToString("N"));
        var localDir = Path.Combine(Path.GetTempPath(), "portway-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localDir);
        try
        {
            await remote.CreateDirectory(remoteDir, ct);
            var path = Path.Combine(localDir, "한글 file ' $ test.txt");
            var bytes = RandomNumberGenerator.GetBytes(600000);
            await File.WriteAllBytesAsync(path, bytes, ct);
            var remotePath = RemotePaths.Join(remoteDir, Path.GetFileName(path));
            await remote.Upload(path, remotePath, 0, _ =>
            {
            }, ct);
            Assert.Contains(await remote.List(remoteDir, ct), f => f.Name == Path.GetFileName(path));
            var download = Path.Combine(localDir, "download.bin");
            await remote.Download(remotePath, download, 0, _ =>
            {
            }, ct);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(download, ct));
            var renamed = RemotePaths.Join(remoteDir, "renamed.txt");
            await remote.Move(remotePath, renamed, ct);
            Assert.Null(await remote.Stat(remotePath, ct));
            Assert.Equal(bytes.Length, (await remote.Stat(renamed, ct))!.Size);
            if (protocol is "sftp" or "scp")
            {
                await remote.Chmod(renamed, "640", ct);
                Assert.Equal("640", (await remote.Stat(renamed, ct))!.Permissions);
            }

            if (protocol == "sftp")
            {
                await remote.Chmod(remoteDir, "1770", ct);
                Assert.Equal("1770", (await remote.Stat(remoteDir, ct))!.Permissions);
            }

            if (remote.Capabilities.Resume)
            {
                File.WriteAllBytes(download, bytes[..100000]);
                await remote.Download(renamed, download, 100000, _ =>
                {
                }, ct);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(download, ct));
            }

            // 수정 시각이 달라도 같은 내용은 동일하게 판단하고 내용이 바뀌면 업로드 하나를 생성합니다.
            File.Delete(path);
            File.Move(download, Path.Combine(localDir, "renamed.txt"));
            var req = new SyncRequest("test", localDir, remoteDir);
            Assert.Empty((await Synchronizer.Preview(remote, req, ct)).Changes);
            await File.WriteAllTextAsync(Path.Combine(localDir, "renamed.txt"), "changed-content", ct);
            var plan = await Synchronizer.Preview(remote, req, ct);
            Assert.Single(plan.Changes);
            await Synchronizer.Apply(remote, req, plan, _ =>
            {
            }, ct);
            Assert.Empty((await Synchronizer.Preview(remote, req, ct)).Changes);
            if (protocol is "sftp" or "scp")
                Assert.Equal("640", (await remote.Stat(renamed, ct))!.Permissions);
            // 오래된 미리보기로 동시에 수정된 원격 파일을 덮어쓰면 안 됩니다.
            await File.WriteAllTextAsync(Path.Combine(localDir, "new.txt"), "new", ct);
            plan = await Synchronizer.Preview(remote, req, ct);
            await File.WriteAllTextAsync(Path.Combine(localDir, "new.txt"), "changed-after-preview", ct);
            await Assert.ThrowsAsync<IOException>(() => Synchronizer.Apply(remote, req, plan, _ =>
            {
            }, ct));
        }
        finally
        {
            await TransferOperations.DeleteTree(remote, remoteDir, CancellationToken.None);
            Directory.Delete(localDir, true);
        }
    }

    [IntegrationFact]
    public async Task SshHostKeyMismatchIsRejected()
    {
        var site = await SiteFor("sftp");
        await using var fs = new RemoteFactory().Create(site with { Fingerprint = "SHA256:" + Convert.ToBase64String(new byte[32]).TrimEnd('=') });
        await Assert.ThrowsAnyAsync<Exception>(() => fs.Connect(CancellationToken.None));
    }

    [IntegrationTheory]
    [InlineData("sftp")]
    [InlineData("ftp")]
    public async Task PausedUploadResumesAndProtectsChangedSource(string protocol)
    {
        var site = await SiteFor(protocol);
        var factory = new RemoteFactory();
        var id = Guid.NewGuid().ToString("N");
        var distant = RemotePaths.Join(site.RemotePath, "resume-" + id);
        var local = Path.Combine(Path.GetTempPath(), "resume-" + id);
        Directory.CreateDirectory(local);
        var path = Path.Combine(local, "data.bin");
        var bytes = RandomNumberGenerator.GetBytes(3000000);
        File.WriteAllBytes(path, bytes);
        var snapshots = new Dictionary<string, Entry>();
        var completed = new HashSet<string>();
        try
        {
            await using (var first = factory.Create(site))
            {
                await first.Connect(CancellationToken.None);
                await first.CreateDirectory(distant, CancellationToken.None);
                using var cancel = new CancellationTokenSource();
                await Assert.ThrowsAnyAsync<Exception>(() => TransferOperations.Transfer(first, "upload", path, distant, "replace", id, (_, p) =>
                {
                    if (p.Bytes >= 131072)
                    {
                        cancel.Cancel();
                        cancel.Token.ThrowIfCancellationRequested();
                    }
                }, cancel.Token, snapshots: snapshots, completed: completed));
            }

            await using var second = factory.Create(site);
            await second.Connect(CancellationToken.None);
            await TransferOperations.Transfer(second, "upload", path, distant, "replace", id, (_, _) =>
            {
            }, CancellationToken.None, snapshots: snapshots, completed: completed);
            var downloaded = Path.Combine(local, "download.bin");
            await second.Download(RemotePaths.Join(distant, "data.bin"), downloaded, 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal(bytes, File.ReadAllBytes(downloaded));
            // 부분 전송 기록이 있는 원본이 수정되면 확인 없이 전송을 이어가면 안 됩니다.
            completed.Clear();
            File.AppendAllText(path, "modified");
            await Assert.ThrowsAsync<IOException>(() => TransferOperations.Transfer(second, "upload", path, distant, "replace", id, (_, _) =>
            {
            }, CancellationToken.None, snapshots: snapshots));
            await TransferOperations.DeleteTree(second, distant, CancellationToken.None);
        }
        finally
        {
            Directory.Delete(local, true);
        }
    }
}
