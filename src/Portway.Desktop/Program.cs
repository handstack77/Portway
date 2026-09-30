using System.Drawing;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Photino.NET;
using Velopack;

namespace Portway.Desktop;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // UI나 파일 작업을 시작하기 전에 이전 실행에서 준비한 패키지를 적용합니다.
        // 이번 실행에서 다운로드한 업데이트는 다음 실행 때 적용합니다.
        VelopackApp.Build().SetAutoApplyOnStartup(true).Run();
        var headless = args.Contains("--headless");
        var token = headless ? Environment.GetEnvironmentVariable("PORTWAY_TOKEN") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (token.Length < 32)
            throw new InvalidOperationException("PORTWAY_TOKEN은 32자 이상이어야 합니다.");
        var port = headless && int.TryParse(Environment.GetEnvironmentVariable("PORTWAY_PORT"), out var value) ? value : 0;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args.Where(a => a != "--headless").ToArray(),
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, port);
            o.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
        });
        builder.Services.AddDesktopServices();
        var app = builder.Build();
        app.UseDesktopSecurity(token);
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapDesktopApi();
        app.StartAsync().GetAwaiter().GetResult();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        if (headless)
        {
            Console.WriteLine("Portway 브라우저 주소: " + address + "/#" + token);
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
        }
        else
        {
#if DEBUG
            const bool devToolsEnabled = true;
#else
            const bool devToolsEnabled = false;
#endif
            // 다른 Photino 앱과 WebView2 사용자 데이터 폴더를 공유하지 않습니다.
            var webViewPath = Path.Combine(app.Services.GetRequiredService<ProfileStore>().DataPath, "webview");
            Directory.CreateDirectory(webViewPath);
            // Windows 제목 표시줄과 Linux 작업 표시줄에 앱 아이콘을 지정합니다. macOS는 앱 번들 아이콘을 사용합니다.
            var iconPath = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "portway.ico" : "portway.png");
            var window = new PhotinoWindow()
                .SetLogVerbosity(0)
                .SetTemporaryFilesPath(webViewPath)
                .SetTitle("Portway · 파일 전송 클라이언트")
                .SetUseOsDefaultSize(false)
                .SetSize(new Size(1440, 940))
                .SetMinSize(980, 680)
                .Center()
                .SetDevToolsEnabled(devToolsEnabled);
            if (File.Exists(iconPath))
                window.SetIconFile(iconPath);
            window.Load(address + "/#" + token);
            app.Services.GetRequiredService<SiteExportService>().Window = window;
            using var stopRegistration = app.Lifetime.ApplicationStopping.Register(() => window.Invoke(window.Close));
            window.WaitForClose();
            stopRegistration.Dispose();
            app.StopAsync().GetAwaiter().GetResult();
        }

        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
