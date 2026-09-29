param([string]$Executable='dist/publish/win-x64/0.3.9/Portway.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$qa=Join-Path $repo ('artifacts/qa/browser-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $qa | Out-Null
$env:PORTWAY_TOKEN=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$env:PORTWAY_PORT="$port"
$env:Portway__DataPath=Join-Path $qa 'profile'
$exe=(Resolve-Path -LiteralPath (Join-Path $repo $Executable)).Path
$process=Start-Process -FilePath $exe -ArgumentList '--headless' -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $qa 'host.log') -RedirectStandardError (Join-Path $qa 'host.err')
@{pid=$process.Id;url="http://127.0.0.1:$port/#$env:PORTWAY_TOKEN";token=$env:PORTWAY_TOKEN;port=$port;directory=$qa}|ConvertTo-Json|Set-Content (Join-Path $repo 'artifacts/browser-qa.json')
Write-Output "브라우저 검증 프로세스 ID: $($process.Id), 포트: $port"
