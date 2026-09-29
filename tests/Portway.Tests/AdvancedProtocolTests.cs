using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
using Portway.Core;
using Portway.Core.Protocols;

namespace Portway.Tests;

public class AdvancedProtocolTests
{
    internal static async Task<Site> SshSite()
    {
        var site = new Site
        {
            Host = "127.0.0.1",
            Port = 22220,
            Username = "portway",
            Password = "portway-test-only",
            RemotePath = "/home/portway/files"
        };
        return site with
        {
            Fingerprint = await SshConnection.Scan(site, CancellationToken.None)
        };
    }

    [IntegrationTheory]
    [InlineData("http")]
    [InlineData("socks4")]
    [InlineData("socks5")]
    public async Task SshProxyConnectsAndStillChecksTargetHostKey(string type)
    {
        var direct = await SshSite();
        var site = direct with
        {
            Port = 22,
            Proxy = new(type, "127.0.0.1", 28088)
        };
        await using var remote = new RemoteFactory().Create(site);
        await remote.Connect(CancellationToken.None);
        Assert.NotNull(await remote.List(site.RemotePath, CancellationToken.None));
        await using var bad = new RemoteFactory().Create(site with { Fingerprint = "SHA256:invalid" });
        await Assert.ThrowsAnyAsync<Exception>(() => bad.Connect(CancellationToken.None));
    }

    [IntegrationFact]
    public async Task SshJumpAuthenticatesBothServersAndSupportsFileOperations()
    {
        var jump = await SshSite();
        var site = jump with
        {
            Port = 22,
            Jump = jump
        };
        Assert.Equal(jump.Fingerprint, await SshConnection.Scan(site, CancellationToken.None));
        await using var remote = new RemoteFactory().Create(site);
        await remote.Connect(CancellationToken.None);
        Assert.Contains("portway", await remote.Command("whoami", CancellationToken.None));
        Assert.NotNull(await remote.List(site.RemotePath, CancellationToken.None));
    }

    sealed class PasswordResponder : IAuthenticationInteraction
    {
        public int Count;
        public Task<string[]> Answer(Site site, string instruction, AuthenticationPrompt[] prompts, CancellationToken ct)
        {
            Count += prompts.Length;
            return Task.FromResult(prompts.Select(_ => "portway-test-only").ToArray());
        }
    }

    [IntegrationFact]
    public async Task KeyboardInteractivePromptsAreAnsweredThroughInteractionContract()
    {
        var responder = new PasswordResponder();
        var site = (await SshSite()) with
        {
            Authentication = "keyboard",
            Password = null
        };
        await using var remote = new RemoteFactory(responder).Create(site);
        await remote.Connect(CancellationToken.None);
        Assert.True(responder.Count > 0);
        Assert.NotNull(await remote.List(site.RemotePath, CancellationToken.None));
    }

    [IntegrationTheory]
    [InlineData("id_ed25519")]
    [InlineData("id_ed25519.ppk")]
    public async Task OpenSshAndPuttyKeysAuthenticate(string keyName)
    {
        var site = await SshSite();
        var file = Path.GetTempFileName();
        try
        {
            await using (var bootstrap = new RemoteFactory().Create(site))
            {
                await bootstrap.Connect(CancellationToken.None);
                await bootstrap.Download("/home/portway/keys/" + keyName, file, 0, _ =>
                {
                }, CancellationToken.None);
            }

            await using var remote = new RemoteFactory().Create(site with { Authentication = "key", Password = null, PrivateKeyPath = file });
            await remote.Connect(CancellationToken.None);
            Assert.NotNull(await remote.List(site.RemotePath, CancellationToken.None));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [IntegrationTheory]
    [InlineData("ftps", 22122)]
    [InlineData("webdavs", 28443)]
    public async Task TlsRejectsUnknownCertificateAndAcceptsOnlyExactPin(string protocol, int port)
    {
        var site = new Site
        {
            Protocol = protocol,
            Host = "127.0.0.1",
            Port = port,
            Username = "portway",
            Password = "portway-test-only"
        };
        var probe = JsonSerializer.SerializeToElement(await TlsOptions.Probe(site, CancellationToken.None));
        var pin = probe.GetProperty("fingerprint").GetString();
        await using (var invalid = new RemoteFactory().Create(site))
            await Assert.ThrowsAnyAsync<Exception>(() => invalid.Connect(CancellationToken.None));
        await using (var bad = new RemoteFactory().Create(site with { TlsFingerprint = "SHA256:" + new string('0', 64) }))
            await Assert.ThrowsAnyAsync<Exception>(() => bad.Connect(CancellationToken.None));
        await using var remote = new RemoteFactory().Create(site with { TlsFingerprint = pin });
        await remote.Connect(CancellationToken.None);
        var path = "/tls-" + Guid.NewGuid().ToString("N") + ".txt";
        var local = Path.GetTempFileName();
        var download = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(local, "verified TLS transfer");
            await remote.Upload(local, path, 0, _ =>
            {
            }, CancellationToken.None);
            await remote.Download(path, download, 0, _ =>
            {
            }, CancellationToken.None);
            Assert.Equal(await File.ReadAllTextAsync(local), await File.ReadAllTextAsync(download));
            await remote.Delete(path, false, CancellationToken.None);
        }
        finally
        {
            File.Delete(local);
            File.Delete(download);
        }
    }

    [IntegrationFact]
    public async Task AgentSignsWithoutExposingPrivateKeyToClient()
    {
        var site = await SshSite();
        await using var bridge = new AgentBridge();
        await using var remote = new RemoteFactory().Create(site with { Authentication = "agent", Password = null, AgentSocket = bridge.Address });
        await remote.Connect(CancellationToken.None);
        Assert.NotNull(await remote.List(site.RemotePath, CancellationToken.None));
    }

    sealed class AgentBridge : IAsyncDisposable
    {
        readonly CancellationTokenSource stop = new();
        readonly Socket? listener;
        readonly Task run;
        public string Address { get; }

        public AgentBridge()
        {
            var name = "portway-agent-" + Guid.NewGuid().ToString("N");
            if (OperatingSystem.IsWindows())
                Address = name;
            else
            {
                Address = Path.Combine(Path.GetTempPath(), name);
                listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listener.Bind(new UnixDomainSocketEndPoint(Address));
                listener.Listen(10);
            }

            run = Task.Run(Serve);
        }

        async Task Serve()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    Stream client;
                    if (OperatingSystem.IsWindows())
                    {
                        var pipe = new NamedPipeServerStream(Address, PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await pipe.WaitForConnectionAsync(stop.Token);
                        client = pipe;
                    }
                    else
                        client = new NetworkStream(await listener!.AcceptAsync(stop.Token), true);
                    await using (client)
                    {
                        using var tcp = new TcpClient();
                        await tcp.ConnectAsync("127.0.0.1", 23022, stop.Token);
                        using var done = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                        var outgoing = client.CopyToAsync(tcp.GetStream(), done.Token);
                        var incoming = tcp.GetStream().CopyToAsync(client, done.Token);
                        await Task.WhenAny(outgoing, incoming);
                        done.Cancel();
                        try
                        {
                            await Task.WhenAll(outgoing, incoming);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener?.Dispose();
            await run;
            stop.Dispose();
            if (!OperatingSystem.IsWindows())
                File.Delete(Address);
        }
    }
}
