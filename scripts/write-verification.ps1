param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Name,
    [Parameter(Mandatory)]
    [object]$Data
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $repo 'doc/Portway.Document/jobs'
$null = [IO.Directory]::CreateDirectory($directory)
$path = Join-Path $directory "verification-$Name.json"
$json = ConvertTo-Json -InputObject $Data -Depth 50

# 새 기록만 생성하여 이전 검증 결과를 덮어쓰지 않습니다.
$stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + "`n")
    $stream.Write($bytes, 0, $bytes.Length)
} finally {
    $stream.Dispose()
}
Write-Output $path
