# 배포 운영

개발 실행·게시 API·패키징의 전체 흐름은 [개발자 가이드](DEVELOPER-GUIDE.md), 앱에서 업데이트를 받는 방법은 [사용자 가이드](USER-GUIDE.md#maintenance)를 참고하세요.

## 따라 하기 전 준비

아래 예시는 **서버 준비 → 대상 OS에서 패키지 생성 → ZIP 검사 → 게시 → 설치·업데이트 확인** 순서입니다. `Portway.Server`는 설치 파일과 업데이트 피드를 제공하며, 하나의 서버에 OS·CPU별 채널을 함께 게시할 수 있습니다.

- 빌드 컴퓨터: .NET 10 SDK, Node.js 20 이상, PowerShell 7. 설치 조건은 [개발자 가이드](DEVELOPER-GUIDE.md)를 따릅니다.
- 서버 컴퓨터: Docker와 Docker Compose. 로컬 연습에서는 Docker Desktop의 Linux 컨테이너를 사용할 수 있습니다.
- 모든 명령은 각 컴퓨터에 체크아웃한 저장소 루트에서 실행합니다. Windows 예시는 `C:\tmp\winscp`, Mac/Linux 예시는 `~/src/portway`를 사용합니다.
- `downloads.example.com`, 인증서 이름, Apple 계정은 실제 값으로 바꿉니다. `0.3.10`과 `0.3.11`은 설명용 버전이며, 기존 릴리스보다 높은 미사용 버전을 선택합니다. 실제 릴리스에서는 [Directory.Build.props](../../../Directory.Build.props)의 `Version`과 [build.ps1](../../../scripts/build.ps1)의 기본 버전도 맞춥니다.

| 패키지를 만들 컴퓨터 | `Runtime` 예시 | stable 채널 | 사용자가 받는 파일 |
| --- | --- | --- | --- |
| Windows x64 | `win-x64` | `win-x64-stable` | `*Setup.exe` |
| Windows ARM64 | `win-arm64` | `win-arm64-stable` | `*Setup.exe` |
| Apple Silicon Mac | `osx-arm64` | `osx-arm64-stable` | `*.pkg` |
| Intel Mac | `osx-x64` | `osx-x64-stable` | `*.pkg` |
| Linux x64 | `linux-x64` | `linux-x64-stable` | `*.AppImage` |
| Linux ARM64 | `linux-arm64` | `linux-arm64-stable` | `*.AppImage` |

패키징 스크립트는 다른 OS의 RID를 거부합니다. Windows 패키지는 Windows에서, macOS 패키지는 Mac에서, Linux 패키지는 Linux에서 생성합니다. CPU가 다른 패키지를 게시하더라도 설치·실행·업데이트는 해당 CPU의 실제 환경에서 확인합니다.

## 1. Portway.Server 준비

### 내 컴퓨터에서 먼저 연습하기

1. Docker를 시작하고 PowerShell에서 다음을 실행합니다. 비밀 관리 도구에서 생성·보관한 **32자 이상 게시 키**를 입력합니다. 입력 내용은 화면에 표시되지 않습니다.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:\tmp\winscp'
$env:PORTWAY_PUBLISH_KEY = Read-Host '비밀 관리 도구에 보관한 게시 키' -MaskInput
if ($env:PORTWAY_PUBLISH_KEY.Length -lt 32) { throw '게시 키는 32자 이상이어야 합니다.' }
docker compose -f compose.yaml up -d --build
if ($LASTEXITCODE -ne 0) { throw '배포 서버 시작에 실패했습니다.' }
Invoke-RestMethod 'http://localhost:5080/healthz'
```

2. `status: ok` 응답을 확인하고 브라우저에서 `http://localhost:5080`을 엽니다. 아직 게시하지 않았다면 릴리스 목록은 비어 있습니다. 이후 빌드·게시 예시의 `$server`를 `http://localhost:5080`으로 지정합니다.
3. Mac/Linux에서 같은 연습을 할 때는 `pwsh`를 실행하고 저장소 위치만 바꿉니다. 이 Compose 구성은 `127.0.0.1:5080`에만 노출되므로 **다른 컴퓨터에서는 접속할 수 없습니다**. 여러 OS 컴퓨터에서 게시하려면 아래 공개 서버를 사용합니다.
4. 다른 터미널에서 게시한다면 서버를 시작할 때 입력한 **같은 키**를 다시 입력합니다. 종료할 때는 같은 저장소에서 `docker compose -f compose.yaml stop`을 실행합니다. 재시작 시에도 같은 키를 환경 변수에 넣습니다. 릴리스 볼륨은 유지합니다.

### Linux 운영 서버에 HTTPS로 올리기

1. 서버에 저장소를 체크아웃하고 Docker/Compose를 준비합니다. 실제 배포 도메인의 DNS A 레코드를 서버 IPv4에 연결합니다. IPv6를 운영한다면 AAAA 레코드도 올바른 서버 주소로 연결합니다. 방화벽에서 TCP 80/443을 열고 다른 서비스가 이 포트를 점유하지 않는지 확인합니다.
2. 서버의 **Bash**에서 아래 명령을 실행합니다. 게시 키는 로컬 연습용과 구분해 비밀 관리 도구에 보관하고, 게시 담당자의 빌드 컴퓨터에도 안전하게 전달합니다.

```bash
cd ~/src/portway
export PORTWAY_DOMAIN=downloads.example.com
read -r -s -p '비밀 관리 도구에 보관한 운영 게시 키: ' PORTWAY_PUBLISH_KEY
printf '\n'
export PORTWAY_PUBLISH_KEY
if [ "${#PORTWAY_PUBLISH_KEY}" -lt 32 ]; then
    printf '게시 키는 32자 이상이어야 합니다.\n' >&2
    exit 1
fi
docker compose -f compose.production.yaml up -d --build
```

3. 상태와 HTTPS를 확인합니다. Caddy가 도메인 인증서를 발급·갱신하며 `distribution:8080`으로 전달합니다. 정상 인증서로 접속되는지 확인하고, 인증서 검사 생략 옵션을 사용하지 않습니다.

```bash
docker compose -f compose.production.yaml ps
curl --fail --show-error https://downloads.example.com/healthz
curl --fail --show-error https://downloads.example.com/api/releases
```

4. 브라우저에서 `https://downloads.example.com`을 엽니다. 이후 빌드 컴퓨터의 `$server`에는 이 주소를 넣습니다. Compose의 `PORTWAY_PUBLISH_KEY`가 서버의 `Distribution__ApiKey`로 전달되므로 게시 도구에도 같은 값을 입력해야 합니다.

서버의 .NET 실행 환경은 [Dockerfile](../../../Dockerfile)에 포함됩니다. Docker 호스트에 앱 빌드용 Node.js를 설치할 필요는 없습니다. 프로덕션 Compose 구성은 [compose.production.yaml](../../../compose.production.yaml), HTTPS 설정은 [Caddyfile](../../../deploy/Caddyfile)을 참고하세요.

## 2. 운영체제별 첫 패키지 만들기

아래 세 예시는 **서버가 실행 중이고 해당 채널에 아직 릴리스가 없는 경우**입니다. 첫 채널이 비어 있음을 `/api/releases`에서 확인한 뒤 `-AllowEmptyChannel`을 사용합니다. 이미 릴리스가 있다면 [다음 버전 게시](#next-release) 명령을 사용합니다. 서버 연결이나 인증서 오류를 빈 채널로 간주하지 않습니다.

`build.ps1`은 npm 의존성 설치·프런트엔드 빌드·테스트, .NET 도구 복원·테스트, Desktop과 CLI 게시, Velopack 패키징을 차례로 수행합니다. 기본 .NET 테스트에서 조건부 통합 검사가 건너뛰어질 수 있으므로 결과의 통과·실패·건너뜀을 구분합니다. 공개 릴리스에서는 `-SkipTests`를 사용하지 않습니다.

### Windows x64: Setup.exe 만들기

1. Windows의 PowerShell 7에서 빌드 값을 지정합니다. 로컬 연습은 `$server = 'http://localhost:5080'`으로 바꿉니다.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location 'C:\tmp\winscp'
$server = 'https://downloads.example.com'
$version = '0.3.10'
$rid = 'win-x64'
$track = 'stable'
$channel = "$rid-$track"
```

2. 공개 배포에 사용할 코드 서명 인증서와 서명 도구 구성을 준비합니다. 아래는 인증서 지문으로 선택하는 예시입니다. 지문과 타임스탬프 URL을 인증서 공급자의 실제 값으로 바꾼 후 실행합니다. 인증서 개인 키의 비밀번호를 명령이나 저장소에 넣지 않습니다.

```powershell
$thumbprint = '인증서의실제지문'
$timestampUrl = 'https://인증서공급자의타임스탬프주소'
$env:PORTWAY_SIGN_PARAMS = "/sha1 $thumbprint /fd SHA256 /tr $timestampUrl /td SHA256"
```

이 환경 변수는 Velopack의 `--signParams`로 전달됩니다. `/sha1`은 인증서 선택에 쓰이고 파일·타임스탬프 해시는 SHA-256을 사용합니다. HSM·클라우드 서명은 공급자 설정에 맞는 서명 인수를 사용합니다. 자세한 옵션은 [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)과 [Velopack 서명 가이드](https://docs.velopack.io/packaging/signing)를 참고하세요. 게시하지 않는 로컬 연습은 서명 변수를 설정하지 않고 진행할 수 있습니다.

3. 첫 패키지를 생성합니다. 완료 후 파일 목록을 확인합니다.

```powershell
./scripts/build.ps1 -Version $version -Runtime $rid -Track $track -PreviousReleaseUrl "$server/releases/$channel/" -AllowEmptyChannel
Get-ChildItem "dist/releases/$channel/$version"
```

4. 결과의 `*Setup.exe`가 사용자 설치 파일입니다. `dist/Portway-0.3.10-win-x64-stable.zip`은 **서버 게시용 ZIP**입니다. 서명한 릴리스라면 아래 결과가 `Valid`인지 확인합니다. 대상 Windows의 별도 검증 계정에서 Setup.exe를 실행하고 앱 창과 포함된 CLI를 확인합니다. 설치된 실행 파일의 서명도 파일 속성에서 확인합니다. WebView2가 없는 환경에서의 설치도 확인합니다.

```powershell
Get-ChildItem "dist/releases/$channel/$version" -Filter '*Setup.exe' | Get-AuthenticodeSignature | Select-Object Path, Status
```

Windows ARM64는 `$rid = 'win-arm64'`로 바꾸고 실제 ARM64 Windows에서 설치·실행을 확인합니다. 이어서 [ZIP 검사와 게시](#publish-release)를 수행합니다.

### macOS Apple Silicon: 서명·공증한 pkg 만들기

1. Mac에 .NET/Node.js/PowerShell과 Xcode 명령줄 도구를 준비합니다. 키체인에 개인 키를 포함한 **Developer ID Application**과 **Developer ID Installer** 인증서가 있어야 합니다. 인증서를 만든 Mac이 아니라면 개인 키를 포함해 안전하게 이전합니다.
2. Mac의 기본 터미널에서 공증 프로필을 등록합니다. 실제 Apple ID와 Team ID로 바꾸고, 암호는 대화식 요청에 입력합니다. Apple ID의 앱 전용 암호를 사용하며 명령 인수에 암호를 적지 않습니다.

```bash
xcrun notarytool store-credentials 'Portway-Notary' \
    --apple-id 'release@example.com' \
    --team-id 'YOURTEAMID'
```

3. `pwsh`를 실행하고 다음을 입력합니다. 인증서 이름의 `Your Organization`을 실제 이름으로 바꿉니다. Velopack 서명 인수의 인증서 이름에는 `(TEAMID)` 접미사를 붙이지 않습니다. 세 환경 변수는 함께 설정합니다. 별도 키체인을 쓴다면 인증서와 공증 프로필을 그 키체인에 등록하고 `PORTWAY_MAC_KEYCHAIN`에 해당 파일의 절대 경로도 지정합니다. [Velopack 서명 가이드](https://docs.velopack.io/packaging/signing)와 [Apple 공증 절차](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow)를 참고하세요.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location "$HOME/src/portway"
$server = 'https://downloads.example.com'
$version = '0.3.10'
$rid = 'osx-arm64'
$track = 'stable'
$channel = "$rid-$track"
$env:PORTWAY_MAC_APP_IDENTITY = 'Developer ID Application: Your Organization'
$env:PORTWAY_MAC_INSTALLER_IDENTITY = 'Developer ID Installer: Your Organization'
$env:PORTWAY_MAC_NOTARY_PROFILE = 'Portway-Notary'
./scripts/build.ps1 -Version $version -Runtime $rid -Track $track -PreviousReleaseUrl "$server/releases/$channel/" -AllowEmptyChannel
Get-ChildItem "dist/releases/$channel/$version"
```

4. 생성한 pkg의 서명과 공증 티켓을 확인합니다. 각 명령이 성공해야 합니다. [Apple의 서명·공증 문제 확인 절차](https://developer.apple.com/documentation/security/resolving-common-notarization-issues)를 참고하세요.

```powershell
$pkg = Get-ChildItem "dist/releases/$channel/$version" -Filter '*.pkg' | Select-Object -First 1
pkgutil --check-signature $pkg.FullName
spctl --assess --type install --verbose $pkg.FullName
xcrun stapler validate $pkg.FullName
```

5. Finder에서 pkg를 열어 별도 검증 환경에 설치하고 앱 시작·파일 전송·CLI 실행을 확인합니다. 게시용 파일은 `dist/Portway-0.3.10-osx-arm64-stable.zip`입니다. Intel Mac은 `$rid = 'osx-x64'`로 바꾸고 실제 Intel Mac에서 같은 확인을 수행합니다. 이어서 [ZIP 검사와 게시](#publish-release)를 수행합니다.

공개 배포용 macOS 패키지는 대상 Mac에서 서명·공증과 설치 검증을 완료합니다. 게시하지 않는 개발 패키지는 서명 환경 변수를 설정하지 않고 만들 수 있습니다. 실제 Apple 인증서와 계정 비밀은 저장소에 포함하지 않습니다.

### Linux x64: AppImage 만들기

1. Linux에서 빌드 도구를 준비합니다. 다음은 저장소의 [Linux 검증 이미지](../../../tests/infrastructure/Dockerfile.linux)와 같은 **Ubuntu 24.04** 패키지 이름을 사용하는 예시입니다. 다른 배포판은 GTK 3, WebKitGTK 4.1, FUSE 2, squashfs 도구에 대응하는 패키지를 확인합니다. GUI 실행에는 실제 그래픽 세션도 필요합니다.

```bash
sudo apt-get update
sudo apt-get install -y libgtk-3-0 libwebkit2gtk-4.1-0 libnotify4 libfuse2t64 squashfs-tools
pwsh
```

2. 열린 PowerShell에서 첫 패키지를 생성합니다.

```powershell
$ErrorActionPreference = 'Stop'
Set-Location "$HOME/src/portway"
$server = 'https://downloads.example.com'
$version = '0.3.10'
$rid = 'linux-x64'
$track = 'stable'
$channel = "$rid-$track"
./scripts/build.ps1 -Version $version -Runtime $rid -Track $track -PreviousReleaseUrl "$server/releases/$channel/" -AllowEmptyChannel
Get-ChildItem "dist/releases/$channel/$version"
```

3. 그래픽 세션의 별도 검증 계정에서 AppImage를 실행합니다. root로 실행하지 않습니다. 앱 창·파일 전송·CLI를 확인합니다. 컨테이너의 headless 검사는 실제 Linux 데스크톱 사용 흐름과 별도로 기록합니다.

```powershell
$appImage = Get-ChildItem "dist/releases/$channel/$version" -Filter '*.AppImage' | Select-Object -First 1
chmod +x $appImage.FullName
& $appImage.FullName
```

게시용 파일은 `dist/Portway-0.3.10-linux-x64-stable.zip`입니다. Linux ARM64는 `$rid = 'linux-arm64'`로 바꾸고 실제 ARM64 그래픽 환경에서 확인합니다. 이어서 아래 게시 절차를 수행합니다.

<a id="publish-release"></a>
## 3. ZIP 검사 후 Portway.Server에 게시

### 생성한 ZIP을 검사하기

1. 위 OS별 예시를 실행한 **같은 PowerShell 터미널**에서 진행합니다. `$server`, `$version`, `$rid`, `$track`, `$channel`이 해당 릴리스 값인지 확인합니다. 터미널을 새로 열었다면 이 값을 다시 지정합니다.
2. 게시용 ZIP의 경로와 SHA-256을 확인합니다. Delta가 있으면 서버에서 해당 `BaseVersion`의 Full과 피드 메타데이터를 새 검증 폴더에 준비합니다. 격리된 테스트 서버에도 기준 Full이 있어야 새 Delta를 게시할 수 있으므로, 다음 버전 검사에서는 이 준비가 필요합니다. 최초 Full만 있는 ZIP은 기준 준비를 생략합니다.

```powershell
$archive = (Resolve-Path "dist/Portway-$version-$rid-$track.zip").Path
Get-FileHash -LiteralPath $archive -Algorithm SHA256
$env:PORTWAY_PACKAGE_TEST = $archive
$env:PORTWAY_PACKAGE_CHANNEL = $channel
$env:PORTWAY_PACKAGE_BASE = $null
$packageFeed = Get-Content "dist/releases/$channel/$version/releases.$channel.json" -Raw | ConvertFrom-Json
$deltas = @($packageFeed.Assets | Where-Object Type -eq 'Delta')
if ($deltas.Count -gt 0) {
    $serverFeed = Invoke-RestMethod "$server/releases/$channel/releases.$channel.json"
    $basis = @($serverFeed.Assets | Where-Object { $_.Type -eq 'Full' -and $_.Version -eq $deltas[0].BaseVersion })
    if ($basis.Count -ne 1) { throw '서버에서 Delta의 기준 Full을 찾을 수 없습니다.' }
    $basisDirectory = Join-Path 'artifacts/qa' ('package-basis-' + [Guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $basisDirectory
    $basisFile = Join-Path $basisDirectory $basis[0].FileName
    Invoke-WebRequest "$server/releases/$channel/$($basis[0].FileName)" -OutFile $basisFile
    if ((Get-Item -LiteralPath $basisFile).Length -ne $basis[0].Size) { throw '기준 Full 크기가 다릅니다.' }
    if ((Get-FileHash -LiteralPath $basisFile -Algorithm SHA256).Hash -ne $basis[0].SHA256) { throw '기준 Full SHA-256이 다릅니다.' }
    @{ Assets = @($basis[0]) } | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $basisDirectory "releases.$channel.json") -Encoding utf8NoBOM
    $env:PORTWAY_PACKAGE_BASE = (Resolve-Path -LiteralPath $basisFile).Path
}
dotnet test tests/Portway.Tests/Portway.Tests.csproj -c Release --filter 'FullyQualifiedName~PackageTests.GeneratedVpkArchivePublishesAndDownloadsWithMatchingHash'
if ($LASTEXITCODE -ne 0) { throw '게시용 ZIP 검사에 실패했습니다.' }
```

이 검사는 실제 ZIP으로 격리된 in-process 서버의 게시·피드·설치 파일 포함·Full 다운로드 해시 일치를 확인합니다. **운영 서버 업로드나 실제 OS 설치·시작·업데이트 검사는 별도로 수행합니다.** Windows의 실제 Delta 복원·손상 시 Full 전환 검사는 `PORTWAY_PACKAGE_BASE`, `PORTWAY_PACKAGE_UPDATER`도 지정해야 합니다. 세부 조건은 [개발자 가이드의 선택 검증](DEVELOPER-GUIDE.md#testing)과 [PackageTests.cs](../../../tests/Portway.Tests/PackageTests.cs)를 참고하세요.

### PowerShell로 게시하기

3. 서버와 같은 관리자 키를 입력하고 ZIP을 게시합니다. 공개 서버는 HTTPS를 사용하고, HTTP는 loopback 로컬 연습에만 허용됩니다.

```powershell
$env:PORTWAY_PUBLISH_KEY = Read-Host '서버에 설정한 게시 키' -MaskInput
./scripts/publish.ps1 -Server $server -Channel $channel -Archive $archive
```

`publish.ps1`은 `POST /api/releases/<채널>`에 `application/zip`과 Bearer 인증을 전달합니다. ZIP을 풀거나 nupkg 하나만 올리지 않습니다. 명령이 실패하면 응답 원인을 해결한 후 다시 확인합니다. 게시 키를 URL·로그·스크린샷에 넣지 않습니다.

4. 공개 목록에서 게시 버전과 채널을 확인합니다.

```powershell
$releases = Invoke-RestMethod "$server/api/releases"
$published = $releases | Where-Object channel -eq $channel
$published.assets | Where-Object Version -eq $version | Format-Table Version, Type, BaseVersion, FileName
$published.downloads | Format-Table name, size, url
```

첫 릴리스에는 Full이 있어야 합니다. 이전 버전을 기준으로 변경분을 생성했다면 새 Full과 Delta가 함께 있고 Delta의 `BaseVersion`이 기준 버전이어야 합니다. 피드는 `GET /releases/<채널>/releases.<채널>.json`, 설치 파일은 `downloads`에 표시된 URL로 받습니다.

5. 공개 서버에서 **이번 버전의 Full**을 새 검증 폴더로 내려받아 피드의 크기·SHA-256과 비교합니다. 이 다운로드에는 게시 키를 전달하지 않습니다.

```powershell
$feed = Invoke-RestMethod "$server/releases/$channel/releases.$channel.json"
$full = @($feed.Assets | Where-Object { $_.Version -eq $version -and $_.Type -eq 'Full' })
if ($full.Count -ne 1) { throw '게시한 버전의 Full 패키지를 확인할 수 없습니다.' }
$qaPath = Join-Path 'artifacts/qa' ('release-download-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $qaPath
$download = Join-Path $qaPath $full[0].FileName
Invoke-WebRequest "$server/releases/$channel/$($full[0].FileName)" -OutFile $download
if ((Get-Item -LiteralPath $download).Length -ne $full[0].Size) { throw '다운로드 크기가 다릅니다.' }
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $full[0].SHA256) { throw '다운로드 SHA-256이 다릅니다.' }
```

6. 대시보드의 설치 파일도 내려받아 해당 OS에서 설치합니다. 이후 앱 설정의 **배포 서버 피드 URL**에는 아래처럼 **채널 디렉터리**를 입력합니다. JSON 파일 URL이나 서버 루트 URL을 입력하지 않습니다.

| 설치본 | 업데이트 피드 URL 예시 |
| --- | --- |
| Windows x64 stable | `https://downloads.example.com/releases/win-x64-stable/` |
| macOS Apple Silicon stable | `https://downloads.example.com/releases/osx-arm64-stable/` |
| Linux x64 stable | `https://downloads.example.com/releases/linux-x64-stable/` |

게시 키는 관리자 업로드에만 사용하며 앱 설정에는 넣지 않습니다. 나머지 RID와 beta도 같은 규칙으로 채널 디렉터리를 지정합니다. 검사가 끝나면 이 터미널에 설정한 `PORTWAY_PACKAGE_TEST`, `PORTWAY_PACKAGE_CHANNEL`, `PORTWAY_PACKAGE_BASE`, `PORTWAY_PUBLISH_KEY` 환경 변수는 필요한 다른 작업이 없다면 제거합니다.

### 웹 대시보드에서 게시하기

명령줄 대신 `https://downloads.example.com`을 열어 **채널**, **배포 API 키**, **배포 ZIP**을 입력하고 **검증 후 게시**를 누를 수 있습니다. 로컬 연습 주소는 `http://localhost:5080`입니다. 채널은 ZIP을 만든 `$channel`과 같아야 합니다. 현재 웹 선택 목록에 없는 beta 채널은 `publish.ps1`으로 게시합니다. 업로드 완료 후 릴리스 목록·다운로드를 위와 동일하게 확인합니다. 다운로드는 공개되어 있으며 키는 브라우저 저장소에 저장하지 않습니다.

<a id="next-release"></a>
## 4. 다음 버전과 변경분 게시

1. 예를 들어 각 OS 채널에 `0.3.10`을 게시·설치했다면 다음 릴리스는 `0.3.11`로 지정합니다. 소스의 버전 기준도 맞춥니다. 이전 OS별 예시의 `$rid`, `$track`, `$server`, 서명 설정을 유지하고 **`-AllowEmptyChannel` 없이** 서버의 같은 채널을 기준으로 빌드합니다.

```powershell
$version = '0.3.11'
$channel = "$rid-$track"
./scripts/build.ps1 -Version $version -Runtime $rid -Track $track -PreviousReleaseUrl "$server/releases/$channel/" -DeltaMode BestSpeed
```

2. 빌드가 표시한 `변경분 기준 버전`이 서버의 최신 Full 버전인지 확인합니다. `-PreviousReleaseUrl`은 최신 Full과 메타데이터를 가져오므로 새 빌드 컴퓨터에서도 사용할 수 있습니다. 게시하기 전에 다른 담당자가 같은 채널의 더 높은 버전을 게시했다면 기준을 다시 확인합니다. 서버는 오래된 기준으로 만든 새 Delta를 거부합니다.
3. [ZIP 검사와 게시](#publish-release)를 새 `$version`으로 다시 수행합니다. ZIP에는 이번 버전만 넣습니다. 서버가 이전 Full/Delta 피드와 합치므로 과거 패키지를 다시 ZIP에 넣을 필요가 없습니다.
4. `0.3.10` 설치본에 같은 채널의 피드 URL을 저장하고 아래 [업데이트 확인](#update-verification)을 진행합니다. 개발 소스 실행만으로 설치본 업데이트 검증을 완료했다고 기록하지 않습니다.

로컬의 이전 릴리스를 기준으로 만들 수도 있습니다. `-PreviousReleaseDirectory`와 `-PreviousReleaseUrl`은 함께 사용할 수 없습니다. 두 옵션을 생략하면 로컬의 같은 채널 릴리스에서 이전 Full을 찾으므로, 운영 게시에는 서버 최신 버전과 일치하는지 확인해야 합니다. 압축률을 우선할 때는 `-DeltaMode BestSize`, 변경분을 만들지 않을 때는 `-DeltaMode None`을 사용합니다.

beta 연습은 `$track = 'beta'`, `$version = '0.3.11-beta.1'`처럼 별도의 버전·채널로 진행합니다. stable 설치본에 beta 피드를 혼용하지 않습니다. `dist/publish/<RID>/<version>`에는 track이 없으므로 같은 작업 공간에서 같은 RID·버전을 track만 바꾸어 다시 빌드할 수 없습니다. 기존 산출물을 삭제·덮어쓰는 대신 미사용 버전이나 별도 작업 공간을 사용합니다. GitHub Actions 자동 빌드·테스트·게시는 설정되어 있지 않습니다.

## 공개 서버

Linux Docker 호스트의 DNS A/AAAA 레코드를 배포 도메인으로 연결하고 TCP 80/443을 열어 둡니다. Caddy가 HTTPS 인증서를 발급하고 갱신합니다.

키는 비밀 관리 도구에 보관하세요. `releases`, `caddy_data`, `caddy_config` 볼륨을 유지합니다. 릴리스 데이터와 별도로 프로덕션 구성을 백업하세요. 키 회전은 환경 변수 변경 후 distribution 컨테이너를 재생성하고 게시 담당자의 키도 함께 갱신합니다. Compose를 다시 실행하기 전에 도메인과 같은 게시 키를 환경 변수에 설정합니다. 릴리스가 있는 서버에서는 `docker compose down -v`로 볼륨을 삭제하지 않습니다.

원하는 OS에서 `scripts/build.ps1`을 실행한 뒤 `scripts/publish.ps1`으로 묶음을 게시합니다. 서버 웹사이트에서도 동일한 ZIP과 관리자 키로 게시할 수 있습니다. 다운로드에는 인증이 필요하지 않습니다. 키는 브라우저 저장소에 저장하지 않습니다.

서버는 다음을 검사합니다:

- 채널 이름 `win|osx|linux` + `x64|arm64` + `stable|beta`
- ZIP 경로 순회, 중복 파일명, 심볼릭 링크, 파일 형식/개수/압축 해제 크기
- `Portway` 패키지 ID, 피드의 패키지 파일 크기와 SHA-1/SHA-256
- 기존 버전 nupkg의 불변성

패키지를 먼저 저장하고 기존 피드와 수신 피드를 버전·형식별로 합친 뒤 피드 파일을 원자적으로 교체합니다. 업로드 ZIP에는 해당 버전만 포함하면 됩니다. 서버는 이전 Full/Delta 목록을 보존하므로 여러 버전을 건너뛰는 변경분 연결도 유지합니다. JSON 피드와 `RELEASES-<채널>` 목록을 함께 갱신합니다. 단일 서버 프로세스에서 채널별 업로드를 직렬화하며 여러 서버 인스턴스가 같은 볼륨에 쓰는 구성은 지원하지 않습니다.

새 변경분에는 `BaseVersion`을 기록합니다. 기준 Full 버전을 먼저 게시하고 현재 서버의 최신 버전을 기준으로 생성해야 합니다. 기준이 없거나 오래된 기준으로 만든 새 변경분은 게시를 거부합니다. 이전 방식의 누적 ZIP도 받을 수 있습니다. 과거 버전을 뒤늦게 게시해도 최신 설치 파일을 이전 것으로 교체하지 않습니다. 같은 버전의 패키지 파일·해시·크기 변경은 거부합니다.

빌드 산출물은 `dist/releases/<RID>-<track>/<version>/`에 생성되며 ZIP에 이전 버전 패키지가 섞이지 않습니다. 서버는 업로드 ZIP 자체를 영구 보관하지 않고 추출한 파일을 서비스합니다. 전체 패키지는 신규 설치와 변경분 실패 시 복구에 필요합니다. 과거 패키지는 자동 삭제하지 않습니다. 운영 보존 정책을 적용할 때는 사용하는 변경분의 기준 Full과 중간 변경분도 함께 보존하세요.

배포 키는 패키지를 게시할 수 있는 관리자 자격 증명입니다. 클라이언트 신뢰는 HTTPS, 서버 접근 통제, OS 코드 서명에 의존합니다. 별도의 오프라인 릴리스 서명 검증 시스템은 구현되어 있지 않습니다.

<a id="update-verification"></a>
## 업데이트 확인

0.3.3부터 피드 URL을 저장한 설치본은 시작할 때 백그라운드에서 새 버전을 확인·다운로드합니다. 실행 중에는 버전을 바꾸지 않고 다음 시작의 Velopack 초기화에서 준비된 패키지를 무인 적용합니다. 피드가 없으면 자동 확인을 생략하고, 네트워크 오류는 앱 실행을 막지 않습니다. 수동 확인·다운로드·즉시 재시작도 유지합니다.

1. 검증할 이전 버전의 패키지를 정상 설치합니다.
2. 같은 RID/채널로 더 높은 새 버전을 빌드하고 서버에 게시합니다.
3. 앱 설정의 업데이트 URL을 해당 채널 디렉터리로 지정합니다.
4. 업데이트 확인 → 다운로드 → 재시작을 수행합니다.
5. 버전 표시, 저장된 연결/설정 보존, 설치/제거를 확인합니다.

설치본의 Velopack UpdateManager는 사용할 수 있는 변경분 연결을 선택해 새 Full 패키지를 복원하고 검증합니다. 기준 패키지가 없거나 변경분이 손상되었거나 변경분 다운로드가 더 비효율적이면 Full 다운로드로 전환합니다. 최초 macOS/Linux 업데이트 등 로컬 Full이 없는 경우에는 전체 다운로드가 필요할 수 있습니다. [Velopack 변경분 문서](https://docs.velopack.io/packaging/deltas)를 참고하세요. 시작 시 백그라운드 준비와 다음 시작의 적용 방식은 동일합니다.

자동 경로는 업데이트 버튼이나 동작 API를 호출하지 않고 별도로 검증합니다. 자동 기능이 있는 이전 버전에 URL을 저장하고 실행하여 다운로드 완료를 기다린 뒤, 현재 버전이 유지되는지 확인합니다. 앱을 종료하고 피드 서버를 끈 상태로 다시 실행해서 새 버전 적용과 프로필 보존을 확인합니다. Windows에서는 [test-automatic-update.ps1](../../../scripts/test-automatic-update.ps1)이 이 흐름을 격리 설치로 검사합니다.

이 설치→버전 변경→재시작 전체 흐름은 사용자 대상 배포 전에 실제 OS별로 수행해야 합니다. 0.3.1에서는 Windows 설치→업데이트→새 Photino 창 실행→제거와 패키지 업로드·피드·해시 일치 다운로드를 검증했습니다. macOS 네이티브 설치·업데이트와 Linux의 전체 설치 업데이트 흐름은 아직 검증하지 않았습니다. 세부 환경과 근거는 [0.3.1 검증 결과](../jobs/VALIDATION-0.3.1.md)를 참고하세요.

0.3.3의 자동 준비·오프라인 다음 시작 적용, 수동 업데이트 회귀와 패키지 검증은 [0.3.3 검증 결과](../jobs/VALIDATION-0.3.3.md)에 기록했습니다. 0.3.4는 이 검증에만 사용한 대상 버전이며 공개 릴리스로 게시하지 않았습니다.

## 문제가 생겼을 때

| 증상 | 확인과 조치 |
| --- | --- |
| 로컬 서버에 다른 컴퓨터에서 접속할 수 없음 | `compose.yaml`은 loopback 전용입니다. 운영 서버의 HTTPS 주소를 사용합니다 |
| HTTPS 인증서 발급·접속 실패 | DNS A/AAAA, 80/443 방화벽, 포트 점유와 Caddy 로그를 확인합니다. 인증서 검증을 끄지 않습니다 |
| `publish.ps1`에서 키 길이 오류 / 서버 HTTP 401 | 게시 컴퓨터의 `PORTWAY_PUBLISH_KEY`가 32자 이상이고 서버에 설정한 값과 같은지 확인합니다 |
| 서버 HTTP 503 | 서버의 `Distribution__ApiKey` 설정을 확인합니다. Compose 환경 변수 설정 후 컨테이너를 재생성합니다 |
| HTTP 400 | 응답의 `detail`을 확인합니다. 채널·ZIP 무결성·버전 불변성·Delta 기준 Full이 서버 최신 버전과 맞는지 확인합니다 |
| HTTP 413 / 415 / 429 | 각각 업로드 2 GB 제한, `application/zip` 형식, 게시 요청 속도 제한을 확인합니다. `publish.ps1`을 사용하고 과도한 재시도를 멈춥니다 |
| 이미 빌드한 버전 / 기존 릴리스와 내용 충돌 | 미사용 새 버전을 지정합니다. 기존 버전의 파일을 덮어쓰거나 삭제하지 않습니다 |
| 최초 피드 404 | `/api/releases`에서 해당 채널이 없는지 확인합니다. 확인된 최초 채널에만 `-AllowEmptyChannel`을 사용합니다 |
| 앱에서 새 버전을 찾지 못함 | 설치본인지, 더 높은 버전인지, OS·CPU·track이 같은지, 채널 디렉터리 URL인지 확인합니다 |
| macOS 서명·공증 실패 | 인증서의 개인 키, 두 Developer ID 인증서, 공증 프로필과 선택 키체인이 일치하는지 확인합니다 |
| Linux에서 창이 뜨지 않음 | GTK/WebKitGTK/FUSE 의존성, 실행 권한과 그래픽 세션을 확인합니다 |

서버 상태는 `docker compose -f compose.production.yaml ps`, 오류는 `docker compose -f compose.production.yaml logs --tail 100 distribution proxy`로 확인합니다. 로컬 구성에서는 파일을 `compose.yaml`로 바꾸고 서비스 이름은 `distribution`만 지정합니다. 공유할 로그·검증 기록에서 비밀을 제거하고, 버전·RID·채널·검사 명령·통과/실패/건너뜀·서명 여부·실제 OS 설치/업데이트 여부를 [작업 기록](../jobs/README.md)에 남깁니다. 이 문서의 예시는 명령과 소스의 대조에 기반하며, 새 버전의 패키징·운영 게시·macOS/Linux 실제 설치가 완료되었다는 검증 결과를 뜻하지 않습니다.
