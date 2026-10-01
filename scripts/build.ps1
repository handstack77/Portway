param(
    [string]$Version = '0.3.9',
    [ValidateSet('win-x64','win-arm64','osx-x64','osx-arm64','linux-x64','linux-arm64')][string]$Runtime,
    [ValidateSet('stable','beta')][string]$Track = 'stable',
    [switch]$SkipTests,
    [string]$PreviousReleaseDirectory,
    [uri]$PreviousReleaseUrl,
    [switch]$AllowEmptyChannel,
    [ValidateSet('BestSpeed','BestSize','None')][string]$DeltaMode = 'BestSpeed'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if (!$Runtime) { $Runtime = "$platform-$([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())" }
if (!$Runtime.StartsWith("$platform-")) { throw 'vpk 패키지는 대상 운영체제에서 생성하세요.' }
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw '버전은 0.1.0 또는 0.3.9-beta.1 같은 SemVer 형식이어야 합니다.' }
function Invoke-Checked { param([string]$Command, [string[]]$Arguments) & $Command @Arguments; if ($LASTEXITCODE -ne 0) { throw "$Command 실행에 실패했습니다. 종료 코드: $LASTEXITCODE" } }
$npmCommand = if ($IsWindows) { 'npm.cmd' } else { 'npm' }
Invoke-Checked $npmCommand @('ci','--prefix','assets/Portway.Artifact/assets/frontend')
Invoke-Checked $npmCommand @('run','build','--prefix','assets/Portway.Artifact/assets/frontend')
Invoke-Checked $npmCommand @('test','--prefix','assets/Portway.Artifact/assets/frontend')
Invoke-Checked dotnet @('tool','restore')
if (!$SkipTests) { Invoke-Checked dotnet @('test','Portway.slnx','-c','Release','--nologo') }
$publish = Join-Path $repo "dist/publish/$Runtime/$Version"
$releases = Join-Path $repo "dist/releases/$Runtime-$Track/$Version"
$zip = Join-Path $repo "dist/Portway-$Version-$Runtime-$Track.zip"
if (Test-Path -LiteralPath $publish) { throw "이미 빌드한 버전입니다: $publish. 새 버전을 사용하거나 이전 빌드를 직접 보관하세요." }
if (Test-Path -LiteralPath $zip) { throw "배포용 압축 파일이 이미 있습니다: $zip. 버전별 산출물은 덮어쓰지 않습니다." }
if (Test-Path -LiteralPath $releases) { throw "버전별 릴리스 디렉토리가 이미 있습니다: $releases" }
Invoke-Checked dotnet @('publish','src/Portway.Desktop','-c','Release','-r',$Runtime,'--self-contained','true','-p:PublishSingleFile=false','-p:PublishTrimmed=false',"-p:Version=$Version",'-o',$publish)
Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination $publish
Invoke-Checked dotnet @('publish','src/Portway.Cli','-c','Release','-r',$Runtime,'--self-contained','true',"-p:Version=$Version",'-o',(Join-Path $publish 'cli'))
& (Join-Path $PSScriptRoot 'package.ps1') -PublishDirectory $publish -Version $Version -Runtime $Runtime -Track $Track -PreviousReleaseDirectory $PreviousReleaseDirectory -PreviousReleaseUrl $PreviousReleaseUrl -AllowEmptyChannel:$AllowEmptyChannel -DeltaMode $DeltaMode
