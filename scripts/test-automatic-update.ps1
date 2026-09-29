param(
    [Parameter(Mandatory)][string]$OldPublish,
    [Parameter(Mandatory)][string]$NewPublish,
    [string]$OldVersion = '0.3.6',
    [string]$NewVersion = '0.3.7'
)
$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw '설치본 자동 업데이트 테스트는 Windows에서 실행해야 합니다.' }
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$id = 'PortwayAutoQa' + [Guid]::NewGuid().ToString('N')
$root = Join-Path $repo "artifacts/qa/$id"
$install = Join-Path $root 'installed'
$profile = Join-Path $root 'profile'
$oldFeed = Join-Path $root 'old'
$feed = Join-Path $root 'feed'
New-Item -ItemType Directory -Path $root, $profile | Out-Null

function FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start(); $number = $listener.LocalEndpoint.Port; $listener.Stop(); return $number
}
function StartHidden([string]$Exe, [string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Exe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    return [Diagnostics.Process]::Start($start)
}
function Checked([string]$Exe, [string[]]$Arguments) {
    $process = StartHidden $Exe $Arguments
    try {
        if (!$process.WaitForExit(180000)) { $process.Kill($true); throw "실행 제한 시간을 초과했습니다: $Exe" }
        if ($process.ExitCode) { throw "$Exe 실행에 실패했습니다. 종료 코드: $($process.ExitCode)" }
    } finally { $process.Dispose() }
}
function InstalledProcesses {
    Get-Process -Name Portway -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $install 'current/Portway.exe') }
}
function StopInstalled {
    foreach ($process in @(InstalledProcesses)) {
        $process.Kill()
        $process.WaitForExit()
        $process.Dispose()
    }
}
function Uninstall {
    $resolved = [IO.Path]::GetFullPath($install)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '제거 대상이 검증 디렉토리를 벗어났습니다.' }
    if (Test-Path -LiteralPath (Join-Path $install 'Update.exe')) {
        Checked (Join-Path $install 'Update.exe') @('--silent', '--rootDir', $resolved, '--log', (Join-Path $root 'uninstall.log'), 'uninstall')
    }
}

$appPort = FreePort
$feedPort = FreePort
$oldEnvironment = @{}
foreach ($name in @('PORTWAY_TOKEN', 'PORTWAY_PORT', 'Portway__DataPath')) { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$env:PORTWAY_TOKEN = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:PORTWAY_PORT = "$appPort"
$env:Portway__DataPath = $profile
$headers = @{ Authorization = "Bearer $env:PORTWAY_TOKEN" }
function Api([string]$Path) { Invoke-RestMethod -Uri "http://127.0.0.1:$appPort/api/$Path" -Headers $headers -TimeoutSec 3 }
function WaitVersion([string]$Version) {
    for ($i = 0; $i -lt 160; $i++) {
        try { if ((Api 'info').version -eq $Version) { return } } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw "설치된 앱의 API 버전이 $Version에 도달하지 않았습니다."
}

$server = $null
$uninstalled = $false
try {
    foreach ($pair in @(@($OldVersion, $OldPublish, $oldFeed), @($NewVersion, $NewPublish, $feed))) {
        $publishPath = (Resolve-Path -LiteralPath $pair[1]).Path
        $actual = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publishPath 'Portway.dll')).Version.ToString(3)
        if ($actual -ne $pair[0]) { throw "게시된 어셈블리 버전 $actual이 예상 버전 $($pair[0])과 일치하지 않습니다." }
        Checked 'dotnet' @('tool', 'run', 'vpk', '--', 'pack', '--packId', $id, '--packVersion', $pair[0], '--packDir', $publishPath, '--mainExe', 'Portway.exe', '--runtime', 'win-x64', '--channel', 'win-x64-stable', '--packTitle', $id, '--outputDir', $pair[2], '--shortcuts', 'None', '--yes', '--skip-updates')
    }
    # 첫 실행 전에 피드를 설정합니다. 업데이트 POST API는 사용하지 않습니다.
    @{ updateUrl = "http://127.0.0.1:$feedPort/"; theme = 'dark'; maxConcurrent = 3 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profile 'preferences.json') -Encoding utf8
    $setup = Get-ChildItem -LiteralPath $oldFeed -Filter '*Setup.exe' | Select-Object -First 1
    Checked $setup.FullName @('--silent', '--installto', $install, '--log', (Join-Path $root 'install.log'))
    $server = StartHidden 'python' @('-m', 'http.server', "$feedPort", '--bind', '127.0.0.1', '--directory', $feed)
    $feedReady = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { $null = Invoke-WebRequest -Uri "http://127.0.0.1:$feedPort/" -TimeoutSec 2; $feedReady = $true; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$feedReady) { throw '검증용 업데이트 피드가 시작되지 않았습니다.' }
    $app = StartHidden (Join-Path $install 'current/Portway.exe') @('--headless')
    WaitVersion $OldVersion
    $ready = $false
    for ($i = 0; $i -lt 480; $i++) {
        $state = Api 'updates'
        if ($state.ready -and $state.version -eq $NewVersion) { $ready = $true; break }
        if ($state.state -eq 'error') { throw '시작 시 업데이트 준비에 실패했습니다.' }
        Start-Sleep -Milliseconds 250
    }
    if (!$ready) { throw '자동 백그라운드 다운로드가 완료되지 않았습니다.' }
    if ($app.HasExited -or (Api 'info').version -ne $OldVersion) { throw '실행 중인 앱이 다음 실행 전에 업데이트를 적용했습니다.' }
    $state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'prepared.json') -Encoding utf8
    StopInstalled
    $app.Dispose()
    # 준비된 패키지는 서버가 오프라인이어도 적용돼야 합니다.
    $server.Kill(); $server.WaitForExit(); $server.Dispose(); $server = $null
    $app = StartHidden (Join-Path $install 'current/Portway.exe') @('--headless')
    WaitVersion $NewVersion
    $prefs = Api 'preferences'
    if ($prefs.theme -ne 'dark' -or $prefs.maxConcurrent -ne 3) { throw '업데이트 후 프로필이 보존되지 않았습니다.' }
    StopInstalled
    $app.Dispose()
    # 업데이트 후 다시 실행해도 업데이트나 재시작을 반복하지 않고 오프라인에서 사용할 수 있어야 합니다.
    $app = StartHidden (Join-Path $install 'current/Portway.exe') @('--headless')
    WaitVersion $NewVersion
    Start-Sleep -Seconds 1
    if ($app.HasExited) { throw '업데이트 후 예상하지 못한 재시작 반복이 발생했습니다.' }
    StopInstalled
    $app.Dispose()
    Uninstall
    if (Test-Path -LiteralPath (Join-Path $install 'current')) { throw '프로그램 제거 후 앱 파일이 남았습니다.' }
    $uninstalled = $true
    $summary = "통과: $OldVersion 설치, 업데이트 API 수동 호출 없이 시작 시 확인·다운로드, 기존 버전 유지, 다음 실행에서 오프라인으로 $NewVersion 적용, 프로필 보존, 이후 오프라인 실행, 프로그램 제거."
    & (Join-Path $PSScriptRoot 'write-verification.ps1') -Name "automatic-update-$id" -Data @{
        date = [DateTimeOffset]::Now.ToString('o')
        host = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        command = 'scripts/test-automatic-update.ps1'
        oldVersion = $OldVersion
        newVersion = $NewVersion
        passed = $true
        summary = $summary
        evidence = "artifacts/qa/$id"
    }
    Write-Output $summary
} finally {
    StopInstalled
    if ($server -and !$server.HasExited) { $server.Kill(); $server.WaitForExit(); $server.Dispose() }
    if (!$uninstalled) { Uninstall }
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
    Write-Output "검증 원본 자료 경로: $root"
}
