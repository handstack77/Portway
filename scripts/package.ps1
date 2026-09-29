param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('win-x64','win-arm64','osx-x64','osx-arm64','linux-x64','linux-arm64')][string]$Runtime,
    [ValidateSet('stable','beta')][string]$Track = 'stable',
    [string]$PreviousReleaseDirectory,
    [uri]$PreviousReleaseUrl,
    [switch]$AllowEmptyChannel,
    [ValidateSet('BestSpeed','BestSize','None')][string]$DeltaMode = 'BestSpeed',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if (!$Runtime.StartsWith("$platform-")) { throw '패키징은 대상 운영체제에서 실행하세요.' }
$releaseVersion = [semver]::Parse($Version)
if ($releaseVersion.ToString() -ne $Version) { throw '정규화된 SemVer 버전을 사용하세요.' }
if ($PreviousReleaseDirectory -and $PreviousReleaseUrl) { throw '이전 릴리스 디렉토리와 URL 중 하나만 지정하세요.' }
if ($PreviousReleaseUrl -and $PreviousReleaseUrl.Scheme -ne 'https' -and !($PreviousReleaseUrl.Scheme -eq 'http' -and $PreviousReleaseUrl.IsLoopback)) { throw '이전 릴리스 URL은 루프백 테스트를 제외하고 HTTPS를 사용하세요.' }
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'Portway.dll')).Version.ToString(3)
if ($assemblyVersion -ne "$($releaseVersion.Major).$($releaseVersion.Minor).$($releaseVersion.Patch)") { throw '게시한 앱의 어셈블리 버전과 패키지 버전이 일치하지 않습니다.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo 'dist' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$channel = "$Runtime-$Track"
$channelDirectory = Join-Path $output "releases/$channel"
$releaseDirectory = Join-Path $channelDirectory $Version
$zipPath = Join-Path $output "Portway-$Version-$channel.zip"
foreach ($target in @($releaseDirectory, $zipPath)) {
    if (Test-Path -LiteralPath $target) { throw "버전별 산출물은 덮어쓰지 않습니다: $target" }
}
$stage = Join-Path $repo ('artifacts/qa/package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $stage
$feedName = "releases.$channel.json"

function Invoke-Checked([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE) { throw "Velopack 실행에 실패했습니다. 종료 코드: $LASTEXITCODE" }
}
function Read-Previous([string]$SearchDirectory) {
    if (!(Test-Path -LiteralPath $SearchDirectory -PathType Container)) { return $null }
    $directories = @((Get-Item -LiteralPath $SearchDirectory)) + @(Get-ChildItem -LiteralPath $SearchDirectory -Directory)
    $candidates = foreach ($candidateDirectory in $directories) {
        $path = Join-Path $candidateDirectory.FullName $feedName
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $feed = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            foreach ($asset in @($feed.Assets | Where-Object { $_.PackageId -eq 'Portway' -and $_.Type -eq 'Full' })) {
                if ($asset.FileName -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{0,199}$' -or $asset.FileName.Contains('..') -or !$asset.FileName.EndsWith('.nupkg')) { throw '이전 피드의 패키지 파일명이 안전하지 않습니다.' }
                if ($asset.SHA1 -notmatch '^[A-Fa-f0-9]{40}$' -or ($asset.SHA256 -and $asset.SHA256 -notmatch '^[A-Fa-f0-9]{64}$')) { throw '이전 피드의 패키지 해시가 잘못되었습니다.' }
                [pscustomobject]@{ Asset=$asset; Version=[semver]::Parse($asset.Version); Directory=$candidateDirectory.FullName }
            }
        }
    }
    return $candidates | Sort-Object Version -Descending | Select-Object -First 1
}

if ($PreviousReleaseUrl) {
    $arguments = @('tool','run','vpk','--','download','http','--url',$PreviousReleaseUrl.AbsoluteUri,'--channel',$channel,'--outputDir',$stage)
    if ($AllowEmptyChannel) { $arguments += '--allowEmptyChannel' }
    Invoke-Checked $arguments
    # vpk download는 Full 파일만 내려받으므로 기준 메타데이터를 피드에서 따로 확보합니다.
    $downloaded = @(Get-ChildItem -LiteralPath $stage -File -Filter '*-full.nupkg')
    if ($downloaded.Count) {
        $feedUrl = [uri]::new($PreviousReleaseUrl.AbsoluteUri.TrimEnd('/') + '/' + $feedName)
        $remoteFeed = Invoke-RestMethod -Uri $feedUrl -TimeoutSec 30
        $basisAssets = @($remoteFeed.Assets | Where-Object { $_.Type -eq 'Full' -and $_.FileName -in $downloaded.Name })
        if ($basisAssets.Count -ne 1) { throw '내려받은 기준 Full 패키지의 피드 정보를 확인할 수 없습니다.' }
        @{ Assets=$basisAssets } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stage $feedName) -Encoding utf8NoBOM
    }
    $previous = Read-Previous $stage
} else {
    $previousDirectory = if ($PreviousReleaseDirectory) { (Resolve-Path -LiteralPath $PreviousReleaseDirectory).Path } else { $channelDirectory }
    $previous = Read-Previous $previousDirectory
    if ($PreviousReleaseDirectory -and !$previous) { throw '지정한 디렉토리에 같은 채널의 이전 Full 패키지 피드가 없습니다.' }
    if ($previous) {
        $source = Join-Path $previous.Directory $previous.Asset.FileName
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw '이전 Full 패키지가 없습니다.' }
        if ((Get-Item -LiteralPath $source).Length -ne $previous.Asset.Size) { throw '이전 Full 패키지의 크기가 일치하지 않습니다.' }
        foreach ($algorithm in @('SHA1','SHA256')) {
            $expected = $previous.Asset.$algorithm
            if ($expected -and (Get-FileHash -LiteralPath $source -Algorithm $algorithm).Hash -ne $expected) { throw "이전 Full 패키지의 $algorithm 해시가 일치하지 않습니다." }
        }
        Copy-Item -LiteralPath $source -Destination $stage
        @{ Assets=@($previous.Asset) } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $stage $feedName) -Encoding utf8NoBOM
    }
}
if ($previous -and $previous.Version -ge $releaseVersion) { throw '새 버전은 이전 릴리스보다 높아야 합니다.' }
if ($previous) { Write-Output "변경분 기준 버전: $($previous.Asset.Version)" }
else { Write-Output '이전 릴리스가 없어 최초 전체 패키지를 생성합니다.' }

$exe = if ($IsWindows) { 'Portway.exe' } else { 'Portway' }
$icon = if ($IsWindows) { 'portway.ico' } elseif ($IsMacOS) { 'portway.icns' } else { 'portway.png' }
$vpkArgs = @('tool','run','vpk','--','pack','--packId','Portway','--packVersion',$Version,'--packDir',$publish,'--mainExe',$exe,'--runtime',$Runtime,'--channel',$channel,'--packTitle','Portway','--packAuthors','Portway contributors','--icon',(Join-Path $repo "assets/$icon"),'--outputDir',$stage,'--delta',$DeltaMode,'--skip-updates','--yes')
if ($IsWindows) {
    $vpkArgs += @('--framework','webview2')
    if ($env:PORTWAY_SIGN_PARAMS) { $vpkArgs += @('--signParams',$env:PORTWAY_SIGN_PARAMS) }
}
if ($IsMacOS) {
    $vpkArgs += @('--bundleId','app.portway.desktop')
    if ($env:PORTWAY_MAC_APP_IDENTITY) {
        $vpkArgs += @('--signAppIdentity',$env:PORTWAY_MAC_APP_IDENTITY,'--signInstallIdentity',$env:PORTWAY_MAC_INSTALLER_IDENTITY,'--notaryProfile',$env:PORTWAY_MAC_NOTARY_PROFILE)
        if ($env:PORTWAY_MAC_KEYCHAIN) { $vpkArgs += @('--keychain',$env:PORTWAY_MAC_KEYCHAIN) }
    }
}
Invoke-Checked $vpkArgs
$feed = Get-Content -LiteralPath (Join-Path $stage $feedName) -Raw | ConvertFrom-Json
$assets = @($feed.Assets | Where-Object { $_.Version -eq $Version })
if (@($assets | Where-Object Type -eq 'Full').Count -ne 1) { throw '현재 버전의 Full 패키지가 정확히 하나 필요합니다.' }
if ($previous -and $DeltaMode -ne 'None' -and @($assets | Where-Object Type -eq 'Delta').Count -ne 1) { throw '이전 버전이 있는데 변경분 패키지가 생성되지 않았습니다.' }
foreach ($asset in $assets | Where-Object Type -eq 'Delta') {
    if (!$previous) { throw '생성된 변경분의 기준 버전을 확인할 수 없습니다.' }
    $asset | Add-Member -NotePropertyName BaseVersion -NotePropertyValue $previous.Asset.Version -Force
}
# vpk의 자산 목록에서 현재 버전의 패키지와 이번에 만든 설치 파일만 선택합니다.
$manifestName = "assets.$channel.json"
$manifest = @(Get-Content -LiteralPath (Join-Path $stage $manifestName) -Raw | ConvertFrom-Json)
$names = @($assets.FileName) + @($manifest | Where-Object Type -in @('Installer','Portable') | ForEach-Object RelativeFileName)
foreach ($name in $names) {
    if ($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{0,199}$' -or $name.Contains('..')) { throw '생성된 자산 파일명이 안전하지 않습니다.' }
    if (!(Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) { throw "생성된 자산이 없습니다: $name" }
}
$null = New-Item -ItemType Directory -Path $releaseDirectory
foreach ($name in $names | Select-Object -Unique) {
    Move-Item -LiteralPath (Join-Path $stage $name) -Destination (Join-Path $releaseDirectory $name)
}
@{ Assets=$assets } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $releaseDirectory $feedName) -Encoding utf8NoBOM
$manifest | ConvertTo-Json -Depth 20 -AsArray | Set-Content -LiteralPath (Join-Path $releaseDirectory $manifestName) -Encoding utf8NoBOM
$legacyLines = foreach ($asset in $assets) { "$($asset.SHA1) $($asset.FileName) $($asset.Size)" }
$legacyLines | Set-Content -LiteralPath (Join-Path $releaseDirectory "RELEASES-$channel") -Encoding utf8NoBOM
[IO.Compression.ZipFile]::CreateFromDirectory($releaseDirectory, $zipPath)
if ($previous) {
    $basisPath = [IO.Path]::GetFullPath((Join-Path $stage $previous.Asset.FileName))
    if (!$basisPath.StartsWith($stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '임시 기준 패키지 경로가 작업 폴더를 벗어났습니다.' }
    if (Test-Path -LiteralPath $basisPath) { Remove-Item -LiteralPath $basisPath }
}
Write-Output "버전별 릴리스 디렉토리: $releaseDirectory"
Write-Output "해당 버전만 포함한 배포 ZIP: $zipPath"
