param(
    [Parameter(Mandatory)][uri]$Server,
    [Parameter(Mandatory)][ValidatePattern('^(win|osx|linux)-(x64|arm64)-(stable|beta)$')][string]$Channel,
    [Parameter(Mandatory)][string]$Archive
)
$ErrorActionPreference = 'Stop'
if ($Server.Scheme -ne 'https' -and !($Server.Scheme -eq 'http' -and $Server.IsLoopback)) { throw '루프백 테스트를 제외하고 HTTPS를 사용하세요.' }
if (!$env:PORTWAY_PUBLISH_KEY -or $env:PORTWAY_PUBLISH_KEY.Length -lt 32) { throw 'PORTWAY_PUBLISH_KEY에 서버 게시 키를 설정하세요. 키는 32자 이상이어야 합니다.' }
$uri = "$($Server.AbsoluteUri.TrimEnd('/'))/api/releases/$Channel"
Invoke-RestMethod -Method Post -Uri $uri -Headers @{ Authorization = "Bearer $env:PORTWAY_PUBLISH_KEY" } -ContentType 'application/zip' -InFile (Resolve-Path -LiteralPath $Archive) -TimeoutSec 1800
