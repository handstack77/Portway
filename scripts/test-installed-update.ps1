param([string]$OldPublish='dist/publish/win-x64/0.1.0',[string]$NewPublish='dist/publish/win-x64/0.3.9',[string]$OldVersion='0.1.0',[string]$NewVersion='0.3.9')
$ErrorActionPreference='Stop'
if (!$IsWindows) { throw 'Windows에서 실행하는 설치 테스트입니다.' }
$repo=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
$id='PortwayQa'+[Guid]::NewGuid().ToString('N')
$root=Join-Path $repo "artifacts/qa/$id"
$install=Join-Path $root 'installed'
$oldFeed=Join-Path $root 'old'
$feed=Join-Path $root 'feed'
New-Item -ItemType Directory -Path $root | Out-Null
function FreePort { $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$number=$listener.LocalEndpoint.Port;$listener.Stop();return $number }
function StartHidden([string]$Exe,[string[]]$Arguments) {
    $start=[Diagnostics.ProcessStartInfo]::new($Exe)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    return [Diagnostics.Process]::Start($start)
}
function Checked([string]$Exe,[string[]]$Arguments) {
    $process=StartHidden $Exe $Arguments
    if(!$process.WaitForExit(180000)){$process.Kill($true);throw "실행 제한 시간을 초과했습니다: $Exe"}
    if($process.ExitCode){throw "$Exe 실행에 실패했습니다. 종료 코드: $($process.ExitCode)"}
    $process.Dispose()
}
foreach($pair in @(@($OldVersion,$OldPublish,$oldFeed),@($NewVersion,$NewPublish,$feed))) {
    Checked 'dotnet' @('tool','run','vpk','--','pack','--packId',$id,'--packVersion',$pair[0],'--packDir',(Resolve-Path -LiteralPath $pair[1]).Path,'--mainExe','Portway.exe','--runtime','win-x64','--channel','win-x64-stable','--packTitle',$id,'--outputDir',$pair[2],'--shortcuts','None','--yes','--skip-updates')
}
$app=$null;$server=$null;$uninstalled=$false
$appPort=FreePort;$feedPort=FreePort
$env:PORTWAY_TOKEN=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:PORTWAY_PORT="$appPort";$env:Portway__DataPath=Join-Path $root 'profile'
$headers=@{Authorization="Bearer $env:PORTWAY_TOKEN"}
function Api([string]$Method,[string]$Path,$Body=$null) {
    $parameters=@{Method=$Method;Uri="http://127.0.0.1:$appPort/api/$Path";Headers=$headers;TimeoutSec=120}
    if($null-ne$Body){$parameters.ContentType='application/json';$parameters.Body=$Body|ConvertTo-Json -Depth 8}
    Invoke-RestMethod @parameters
}
function WaitApi([string]$Version) {
    for($i=0;$i-lt 80;$i++){try{$info=Api GET info;if($info.version-eq$Version){return}}catch{};Start-Sleep -Milliseconds 250}
    throw "설치한 $Version 버전의 앱이 시작되지 않았습니다."
}
function Uninstall {
    $resolved=[IO.Path]::GetFullPath($install)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath($root)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '제거 대상이 검증 작업 공간을 벗어났습니다.'}
    if(Test-Path -LiteralPath (Join-Path $install 'Update.exe')){Checked (Join-Path $install 'Update.exe') @('--silent','--rootDir',$resolved,'--log',(Join-Path $root 'uninstall.log'),'uninstall')}
}
try {
    $setup=Get-ChildItem -LiteralPath $oldFeed -Filter '*Setup.exe'|Select-Object -First 1
    Checked $setup.FullName @('--silent','--installto',$install,'--log',(Join-Path $root 'install.log'))
    if(!(Test-Path -LiteralPath (Join-Path $install 'current/Portway.exe'))){throw '설치한 실행 파일이 없습니다.'}
    $server=StartHidden 'python' @('-m','http.server',"$feedPort",'--bind','127.0.0.1','--directory',$feed)
    $app=StartHidden (Join-Path $install 'current/Portway.exe') @('--headless')
    WaitApi $OldVersion
    $prefs=Api GET preferences;$prefs.updateUrl="http://127.0.0.1:$feedPort/";$null=Api PUT preferences $prefs
    $check=Api POST updates/check
    if(!$check.installed-or!$check.available-or$check.version-ne$NewVersion){throw "예상하지 못한 업데이트 결과입니다: $($check|ConvertTo-Json -Compress)"}
    $null=Api POST updates/download
    try{$null=Api POST updates/apply}catch{if(!$app.HasExited){Start-Sleep -Milliseconds 500;if(!$app.HasExited){throw}}}
    if(!$app.WaitForExit(10000)){throw '업데이트를 위해 이전 앱이 종료되지 않았습니다.'}
    $native=$null
    for($i=0;$i-lt 120;$i++){
        $native=Get-Process -Name Portway -ErrorAction SilentlyContinue|Where-Object { $_.Path -eq (Join-Path $install 'current/Portway.exe') -and $_.Id -ne $app.Id -and $_.MainWindowTitle -like '*Portway*' }|Select-Object -First 1
        if($native){break};Start-Sleep -Milliseconds 250
    }
    if(!$native){throw '업데이트된 앱이 Photino 창을 표시하며 다시 시작되지 않았습니다.'}
    if(!$native.CloseMainWindow()){throw '업데이트된 앱 창을 닫지 못했습니다.'}
    if(!$native.WaitForExit(10000)){throw '창을 닫은 뒤에도 업데이트된 앱이 종료되지 않았습니다.'}
    $app.Dispose();$app=StartHidden (Join-Path $install 'current/Portway.exe') @('--headless');WaitApi $NewVersion
    $app.Kill();$app.WaitForExit();$app.Dispose();$app=$null
    Uninstall
    for($i=0;$i-lt 40-and(Test-Path -LiteralPath (Join-Path $install 'current'));$i++){Start-Sleep -Milliseconds 250}
    if(Test-Path -LiteralPath (Join-Path $install 'current')){throw '프로그램 제거 후 설치 파일이 남았습니다.'}
    $uninstalled=$true
    $summary = "통과: 격리된 $OldVersion 설치, 앱 피드 확인·다운로드·적용, Photino 재시작, $NewVersion API 확인, 프로그램 제거."
    & (Join-Path $PSScriptRoot 'write-verification.ps1') -Name "installed-update-$id" -Data @{
        date = [DateTimeOffset]::Now.ToString('o')
        host = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        command = 'scripts/test-installed-update.ps1'
        oldVersion = $OldVersion
        newVersion = $NewVersion
        passed = $true
        summary = $summary
        evidence = "artifacts/qa/$id"
    }
    Write-Output $summary
} finally {
    if($app-and!$app.HasExited){$app.Kill();$app.WaitForExit()}
    Get-Process -Name Portway -ErrorAction SilentlyContinue|Where-Object { $_.Path -eq (Join-Path $install 'current/Portway.exe') }|Stop-Process
    if($server-and!$server.HasExited){$server.Kill();$server.WaitForExit()}
    if(!$uninstalled){Uninstall}
    Write-Output "검증 원본 자료 경로: $root"
}
