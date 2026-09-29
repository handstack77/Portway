# Third-party components

Portway uses the following independently maintained packages. The distributed application does not bundle or invoke WinSCP. Optional interoperability tests invoke an independently downloaded official WinSCP package; that package is not redistributed.

| Component | Version | License / source |
|---|---|---|
| .NET / ASP.NET Core | 10 | MIT, https://github.com/dotnet/runtime and https://github.com/dotnet/aspnetcore |
| Photino.NET | 4.0.16 | Apache-2.0, https://github.com/tryphotino/photino.NET |
| Photino.Native | 4.0.22 | Apache-2.0, https://github.com/tryphotino/photino.Native |
| SSH.NET | 2026.0.0 | MIT, https://github.com/sshnet/SSH.NET |
| SshNet.Agent | 2026.0.0 | MIT, https://github.com/darinkes/SshNet.Agent |
| xterm.js / addon-fit | 6.0.0 / 0.11.0 | MIT, https://github.com/xtermjs/xterm.js; license copies in wwwroot/vendor |
| Monaco Editor | 0.57.0 | MIT, https://github.com/microsoft/monaco-editor; LICENSE and ThirdPartyNotices.txt in wwwroot/vendor/monaco |
| esbuild (build only) | 0.28.2 | MIT, https://github.com/evanw/esbuild |
| Prettier (development only) | 3.9.9 | MIT, https://github.com/prettier/prettier; formatter only, not shipped with the app |
| Noto Sans KR Variable (Fontsource) | 5.3.0 | SIL OFL-1.1, https://fontsource.org/fonts/noto-sans-kr; unicode-split WOFF2 and license in wwwroot/vendor/noto-sans-kr |
| Tabler CSS | 1.4.0 | MIT, https://github.com/tabler/tabler; release license copied to wwwroot/vendor/tabler/LICENSE |
| Tabler Icons webfont | 3.48.0 | MIT, https://github.com/tabler/tabler-icons; font and license copied to wwwroot/vendor/tabler-icons |
| Master CSS | 1.37.8 | MIT, https://github.com/master-co/css; build-time static CSS renderer, license in wwwroot/vendor/master |
| BouncyCastle.Cryptography | 2.7.0 | MIT, https://github.com/bcgit/bc-csharp |
| FluentFTP | 55.0.0 | MIT, https://github.com/robinrodricks/FluentFTP |
| AWSSDK.S3 | 4.0.103.4 | Apache-2.0, https://github.com/aws/aws-sdk-net |
| Velopack | 1.2.158 | MIT, https://github.com/velopack/velopack |

Native web views use the operating system's WebView2, WebKit or WebKitGTK components and their respective licenses. Development test fixtures are not distributed with the app. Exact transitive dependencies and license metadata can be inspected using `dotnet list package --include-transitive` and the referenced NuGet package metadata. Preserve upstream license and NOTICE files when redistributing binaries.
