param([Parameter(Mandatory)][string]$Executable)
# tests/infrastructure/compose.yaml의 루프백 전용 SSH 테스트 서버가 필요합니다.
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$repo = Split-Path $PSScriptRoot -Parent
$testId = [Guid]::NewGuid().ToString('N')
$work = Join-Path $repo "artifacts/qa/api-$testId"
New-Item -ItemType Directory -Path $work | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$base = "http://127.0.0.1:$port"
$token = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$headers = @{ Authorization = "Bearer $token" }
$previous = @{}
foreach ($name in @('PORTWAY_TOKEN','PORTWAY_PORT','Portway__DataPath')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$appProcess = $null
$session = $null
$remotePath = "/home/portway/files/api-$testId.txt"
function Api([string]$Method, [string]$Path, $Body = $null) {
    $args = @{ Method=$Method; Uri="$base/api/$Path"; Headers=$headers; TimeoutSec=30 }
    if ($null -ne $Body) { $args.ContentType='application/json'; $args.Body=($Body | ConvertTo-Json -Depth 8 -Compress) }
    Invoke-RestMethod @args
}
try {
    $env:PORTWAY_TOKEN = $token
    $env:PORTWAY_PORT = "$port"
    $env:Portway__DataPath = Join-Path $work 'profile'
    $start = @{ FilePath=$exe; ArgumentList='--headless'; PassThru=$true; RedirectStandardOutput=(Join-Path $work 'host.log'); RedirectStandardError=(Join-Path $work 'host.err') }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $appProcess = Start-Process @start
    $ready = $false
    for ($i=0; $i -lt 60; $i++) {
        try { $null = Api GET info; $ready = $true; break } catch { if ($appProcess.HasExited) { throw '데스크톱 호스트가 종료되었습니다.' }; Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw '데스크톱 호스트가 준비되지 않았습니다.' }
    $unauthorized = Invoke-WebRequest "$base/api/info" -SkipHttpErrorCheck
    if ($unauthorized.StatusCode -ne 401) { throw '인증되지 않은 API 요청이 거부되지 않았습니다.' }
    $crossOrigin = Invoke-WebRequest "$base/api/info" -Headers @{ Authorization="Bearer $token"; Origin='https://untrusted.example' } -SkipHttpErrorCheck
    if ($crossOrigin.StatusCode -ne 403) { throw '외부 Origin 요청이 거부되지 않았습니다.' }
    $site = @{ name='API fixture'; protocol='sftp'; host='127.0.0.1'; port=22220; username='portway'; password='portway-test-only'; remotePath='/home/portway/files' }
    $site.fingerprint = (Api POST fingerprint $site).fingerprint
    $session = Api POST sessions $site
    $local = Join-Path $work "api-$testId.txt"
    [IO.File]::WriteAllText($local, 'initial content')
    $job = Api POST transfers @{ sessionId=$session.id; direction='upload'; paths=@($local); destination='/home/portway/files'; conflict='skip' }
    $status = $null
    for ($i=0; $i -lt 100; $i++) {
        $allJobs = Api GET transfers
        $status = $allJobs | Where-Object { $_.id -eq $job.id }
        if ($status.status -in @('completed','failed','cancelled')) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($status.status -ne 'completed') { throw "전송 큐 업로드에 실패했습니다. 요청: $($job | ConvertTo-Json -Compress); 실제 상태: $(Api GET transfers | ConvertTo-Json -Depth 4 -Compress)" }
    $null = Api POST "remote/$($session.id)/chmod" @{ path=$remotePath; octal='640' }
    $original = Api POST "remote/$($session.id)/read" @{ path=$remotePath }
    if ($original.content -ne 'initial content') { throw '원격 편집기가 잘못된 내용을 반환했습니다.' }
    $null = Api POST "remote/$($session.id)/write" @{ path=$remotePath; etag=$original.etag; content='edited content' }
    $edited = Api POST "remote/$($session.id)/read" @{ path=$remotePath }
    if ($edited.content -ne 'edited content') { throw '원격 편집기가 파일을 저장하지 않았습니다.' }
    $listing = Api GET "remote/$($session.id)?path=/home/portway/files"
    $entry = $listing.entries | Where-Object path -EQ $remotePath
    if ($entry.permissions -ne '640') { throw '원격 편집기가 파일 권한을 변경했습니다.' }
    $stale = Invoke-WebRequest "$base/api/remote/$($session.id)/write" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{ path=$remotePath; etag=$original.etag; content='stale edit' } | ConvertTo-Json -Compress) -SkipHttpErrorCheck
    if ($stale.StatusCode -ne 409) { throw '오래된 원격 수정 요청이 거부되지 않았습니다.' }
    $summary = '통과: API 토큰 인증, Origin 격리, 전송 큐 SFTP 업로드, 원격 편집기, 권한 보존, 오래된 수정 거부.'
    & (Join-Path $PSScriptRoot 'write-verification.ps1') -Name "desktop-$testId" -Data @{
        date = [DateTimeOffset]::Now.ToString('o')
        host = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        command = 'scripts/test-desktop.ps1'
        executable = $exe
        passed = $true
        summary = $summary
        evidence = "artifacts/qa/api-$testId"
    }
    Write-Output $summary
} finally {
    if ($session) {
        try { $null = Api POST "remote/$($session.id)/delete" @{ path=$remotePath }; $null = Api DELETE "sessions/$($session.id)" } catch { Write-Warning "테스트 서버 정리 중 오류: $($_.Exception.Message)" }
    }
    if ($appProcess -and !$appProcess.HasExited) { $appProcess.Kill(); $appProcess.WaitForExit() }
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
