param([switch]$KeepFixtures)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path $PSScriptRoot -Parent)
try {
    docker compose -p portway-tests -f tests/infrastructure/compose.yaml up -d --build
    if ($LASTEXITCODE) { throw '테스트 서버 시작에 실패했습니다.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        $log = docker compose -p portway-tests -f tests/infrastructure/compose.yaml logs 2>&1 | Out-String
        if ($log.Contains('PORTWAY 테스트 서버 준비 완료')) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw '프로토콜 테스트 서버가 준비되지 않았습니다.' }
    $env:PORTWAY_INTEGRATION = '1'
    dotnet test Portway.slnx -c Release --logger 'console;verbosity=normal'
    if ($LASTEXITCODE) { throw '통합 테스트에 실패했습니다.' }
    & (Join-Path $PSScriptRoot 'write-verification.ps1') -Name ('integration-' + [Guid]::NewGuid().ToString('N')) -Data @{
        date = [DateTimeOffset]::Now.ToString('o')
        host = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        command = 'scripts/test-integration.ps1'
        passed = $true
        summary = '격리된 프로토콜 서버를 사용하는 dotnet test 실행에 성공했습니다. 개별 통과·건너뜀 수치는 콘솔 결과를 확인하세요.'
    }
} finally {
    Remove-Item Env:\PORTWAY_INTEGRATION -ErrorAction SilentlyContinue
    if (!$KeepFixtures) { docker compose -p portway-tests -f tests/infrastructure/compose.yaml down }
}
