using System.Reflection;
using System.Security.Cryptography;
using Portway.Core;
using Portway.Desktop;
using Microsoft.Extensions.Configuration;

namespace Portway.Tests;

public sealed class WinScpReferenceFactAttribute : FactAttribute
{
    public WinScpReferenceFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("PORTWAY_WINSCP_REFERENCE") == null)
            Skip = "Set PORTWAY_WINSCP_REFERENCE to extracted official WinSCP NuGet package on Windows with fixtures running.";
    }
}

public class EncryptionTests
{
    [Fact]
    public void SavedEncryptionKeysRemainProtectedAndCanBeEditedAfterUnlock()
    {
        var root = Path.Combine(Path.GetTempPath(), "portway-key-vault-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var profiles = new ProfileStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Portway:DataPath"] = root }).Build());
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var site = new Site
            {
                Host = "localhost",
                SavePassword = true,
                EncryptionKey = key
            };
            profiles.Unlock("encryption-key-vault-password");
            profiles.Save(site);
            var saved = Assert.Single(profiles.List());
            Assert.True(saved.EncryptFiles);
            Assert.Null(saved.EncryptionKey);
            profiles.Save(saved with { Note = "edited metadata" });
            Assert.Equal(key, profiles.Hydrate(saved).EncryptionKey);
            Assert.DoesNotContain(key, File.ReadAllText(Path.Combine(root, "sites.json")));
            profiles.Lock();
            Assert.Throws<InvalidOperationException>(() => profiles.Hydrate(saved));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CounterModeMatchesNistAes256Vector()
    {
        var key = Convert.FromHexString("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");
        var iv = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");
        var data = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51");
        WinScpEncryption.Transform(data, key, iv);
        Assert.Equal("601EC313775789A5B7A7F504BBF3D228F443E3CA4D62B59ACA84E990CACAF5C5", Convert.ToHexString(data));
        var encoded = WinScpEncryption.EncryptName("한글 file.txt", key);
        Assert.Equal("한글 file.txt", WinScpEncryption.DecryptName(encoded, key));
        Assert.NotEqual(encoded, WinScpEncryption.EncryptName("한글 file.txt", key));
    }

    [IntegrationFact]
    public async Task TransparentEncryptionPreservesPlaintextExistingFilesAndEncryptsNewFilesAndDirectories()
    {
        var site = await AdvancedProtocolTests.SshSite();
        var root = Path.Combine(Path.GetTempPath(), "portway-crypto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = "/home/portway/files/crypto-" + Guid.NewGuid().ToString("N");
        await using var raw = new RemoteFactory().Create(site);
        await raw.Connect(CancellationToken.None);
        await raw.CreateDirectory(path, CancellationToken.None);
        await using var encrypted = new RemoteFactory().Create(site with { EncryptionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) });
        await encrypted.Connect(CancellationToken.None);
        try
        {
            var local = Path.Combine(root, "secret.txt");
            File.WriteAllText(local, "secret 한글");
            await encrypted.Upload(local, path + "/secret.txt", 0, _ =>
            {
            }, CancellationToken.None);
            Assert.EndsWith(WinScpEncryption.Extension, Assert.Single(await raw.List(path, CancellationToken.None)).Name);
            Assert.Equal(new FileInfo(local).Length, (await encrypted.Stat(path + "/secret.txt", CancellationToken.None))!.Size);
            var copy = Path.Combine(root, "copy");
            await encrypted.Download(path + "/secret.txt", copy, 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal(File.ReadAllBytes(local), File.ReadAllBytes(copy));
            await encrypted.CreateDirectory(path + "/폴더", CancellationToken.None);
            await encrypted.Move(path + "/secret.txt", path + "/폴더/renamed.txt", CancellationToken.None);
            Assert.NotNull(await encrypted.Stat(path + "/폴더/renamed.txt", CancellationToken.None));
            await raw.Upload(local, path + "/secret.txt", 0, _ =>
            {
            }, CancellationToken.None);
            File.WriteAllText(local, "updated plain");
            await TransferOperations.Transfer(encrypted, "upload", local, path, "replace", Guid.NewGuid().ToString("N"), (_, _) =>
            {
            }, CancellationToken.None);
            Assert.NotNull(await raw.Stat(path + "/secret.txt", CancellationToken.None));
            await raw.Download(path + "/secret.txt", copy, 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal("updated plain", File.ReadAllText(copy));
            var sanitized = SiteSecrets.Public(site with { EncryptionKey = new string('a', 64) });
            Assert.True(sanitized.EncryptFiles);
            Assert.Throws<ArgumentException>(() => new RemoteFactory().Create(sanitized));
        }
        finally
        {
            await TransferOperations.DeleteTree(raw, path, CancellationToken.None);
            Directory.Delete(root, true);
        }
    }

    [WinScpReferenceFact]
    public async Task OfficialWinScp657ReadsPortwayEncryptedFilesAndPortwayReadsWinScpFiles()
    {
        var package = Environment.GetEnvironmentVariable("PORTWAY_WINSCP_REFERENCE")!;
        var assembly = Assembly.LoadFrom(Path.Combine(package, "lib/netstandard2.0/WinSCPnet.dll"));
        dynamic reference = Activator.CreateInstance(assembly.GetType("WinSCP.Session")!)!;
        dynamic options = Activator.CreateInstance(assembly.GetType("WinSCP.SessionOptions")!)!;
        options.HostName = "127.0.0.1";
        options.PortNumber = 22220;
        options.UserName = "portway";
        options.Password = "portway-test-only";
        reference.ExecutablePath = Path.Combine(package, "tools/WinSCP.exe");
        options.SshHostKeyFingerprint = reference.ScanFingerprint(options, "SHA-256");
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        options.AddRawSettings("EncryptKeyPlain", key);
        var site = (await AdvancedProtocolTests.SshSite()) with
        {
            EncryptionKey = key
        };
        var path = "/home/portway/files/interop-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "portway-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var raw = new RemoteFactory().Create(site with { EncryptionKey = null });
        await raw.Connect(CancellationToken.None);
        await raw.CreateDirectory(path, CancellationToken.None);
        await using var encrypted = new RemoteFactory().Create(site);
        await encrypted.Connect(CancellationToken.None);
        try
        {
            reference.Open(options);
            var input = Path.Combine(root, "portway.txt");
            var bytes = RandomNumberGenerator.GetBytes(131077);
            File.WriteAllBytes(input, bytes);
            await encrypted.Upload(input, path + "/portway.txt", 0, _ =>
            {
            }, CancellationToken.None);
            reference.GetFiles(path + "/portway.txt", Path.Combine(root, "read-by-winscp.bin")).Check();
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "read-by-winscp.bin")));
            reference.PutFiles(input, path + "/winscp.txt").Check();
            await encrypted.Download(path + "/winscp.txt", Path.Combine(root, "read-by-portway.bin"), 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "read-by-portway.bin")));
        }
        finally
        {
            reference.Dispose();
            await TransferOperations.DeleteTree(raw, path, CancellationToken.None);
            Directory.Delete(root, true);
        }
    }
}
