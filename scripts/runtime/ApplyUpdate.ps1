$ErrorActionPreference='Stop'

function Resolve-OwnedUpdatePath([string]$Base,[string]$Relative){
 if(!$Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '[:\x00-\x1f]'){throw 'Invalid update path.'}
 $basePath=[IO.Path]::GetFullPath($Base).TrimEnd('\','/')
 $full=[IO.Path]::GetFullPath((Join-Path $basePath $Relative))
 if(!$full.StartsWith($basePath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Update path escapes its directory.'}
 for($path=$full;$path;$path=[IO.Path]::GetDirectoryName($path)){
  if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Update does not follow linked files or directories.'}
 }
 return $full
}
function Read-UpdateJson([string]$File,[int]$Limit=8192){
 if(!(Test-Path -LiteralPath $File -PathType Leaf)){return}
 if((Get-Item -LiteralPath $File).Length -gt $Limit){throw 'Update record exceeds its size limit.'}
 return [IO.File]::ReadAllText($File)|ConvertFrom-Json
}
function Get-UpdateCandidate($Stage,[string]$Current){
 if(!$Stage -or $Stage.version -cnotmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}$' -or $Stage.tag -cne ('v'+$Stage.version) -or $Stage.name -cne ('PrismGlass-Setup-'+$Stage.version+'-x64.exe') -or $Stage.sha256 -cnotmatch '^[0-9a-f]{64}$' -or ($Stage.size -isnot [int] -and $Stage.size -isnot [long]) -or $Stage.size -le 0 -or $Stage.size -gt 256MB){throw 'Invalid staged installer metadata.'}
 if($Current -cnotmatch '^(?<core>\d{1,5}\.\d{1,5}\.\d{1,5})(?<preview>-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$'){throw 'The installed version cannot be verified.'}
 $v=[version]$Stage.version
 $installedCore=[version]$Matches.core
 if($v -lt $installedCore -or ($v -eq $installedCore -and !$Matches.preview)){throw 'Only a newer formal release may be installed automatically.'}
 return $v
}
function Test-OfficialUpdateAsset($Stage,$Release){
 if(!$Release -or $Release.draft -isnot [bool] -or $Release.prerelease -isnot [bool] -or $Release.draft -or $Release.prerelease -or $Release.tag_name -cne $Stage.tag){return $false}
 $download='https://github.com/liburce0412-alt/prismglass-desktop/releases/download/'+$Stage.tag+'/'+$Stage.name
 $matches=@($Release.assets|Where-Object {$_.name -ceq $Stage.name})
 return $matches.Count -eq 1 -and $matches[0].state -ceq 'uploaded' -and $matches[0].browser_download_url -ceq $download -and $matches[0].size -eq $Stage.size -and $matches[0].digest -ceq ('sha256:'+$Stage.sha256)
}
function Read-OfficialUpdateRelease {
 $request=[Net.HttpWebRequest]::Create('https://api.github.com/repos/liburce0412-alt/prismglass-desktop/releases/latest')
 $request.AllowAutoRedirect=$false;$request.Timeout=8000;$request.ReadWriteTimeout=8000;$request.UserAgent='PrismGlass-Desktop-AutoUpdate'
 $request.Accept='application/vnd.github+json'
 $response=$request.GetResponse()
 try{
  if([int]$response.StatusCode -ne 200 -or $response.ContentLength -gt 1MB){throw 'GitHub release verification returned an unexpected response.'}
  $stream=$response.GetResponseStream();$body=[IO.MemoryStream]::new()
  try{
   $buffer=New-Object byte[] 16384;$deadline=[DateTime]::UtcNow.AddSeconds(12)
   while(($count=$stream.Read($buffer,0,$buffer.Length)) -gt 0){if($body.Length+$count -gt 1MB -or [DateTime]::UtcNow -gt $deadline){throw 'GitHub release verification exceeded its limits.'};$body.Write($buffer,0,$count)}
   return [Text.Encoding]::UTF8.GetString($body.ToArray())|ConvertFrom-Json
  }finally{$body.Dispose();$stream.Dispose()}
 }finally{$response.Dispose()}
}
function Read-ActiveUpdateOwner([string]$File,[string]$Exe){
 $data=Read-UpdateJson $File
 if(!$data -or $data.pid -isnot [ValueType] -or $data.startTicks -cnotmatch '^\d{10,20}$' -or $data.state -notin 'starting','running','stopping','detached'){return}
 $process=Get-Process -Id $data.pid -ErrorAction SilentlyContinue
 if(!$process){return}
 try{
  if(!$process.HasExited -and $process.Path -eq $Exe -and $process.SessionId -eq $script:updateSession -and $data.startTicks -ceq [string]$process.StartTime.ToUniversalTime().Ticks){return $data}
 }catch [InvalidOperationException]{}finally{$process.Dispose()}
}
function Test-UpdateCanStop($Desktop,$Companion){
 if($Desktop -and ($Desktop.state -ne 'running' -or $Desktop.companionRunning)){return $false}
 if($Companion -and ($Companion.state -ne 'running' -or $Companion.active -isnot [bool] -or $Companion.active)){return $false}
 return $true
}
function Invoke-UpdateWorker([string]$File,[string[]]$Arguments,[int]$Timeout){
 $process=Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
 try{if(!$process.WaitForExit($Timeout)){throw 'The update step has not finished.'};return $process.ExitCode}finally{$process.Dispose()}
}
function Wait-UpdateReopen([string]$File,[string]$Executable,[bool]$Companion,[DateTime]$Requested,[bool]$ListenerExpected=$true){
 $until=[DateTime]::UtcNow.AddSeconds(45)
 do{
  $owner=Read-ActiveUpdateOwner $File $Executable
  if($owner -and $owner.state -eq 'running'){
   if($Companion -and (!$ListenerExpected -or $owner.companionRunning)){return $true}
   if(!$Companion -and [IO.File]::GetLastWriteTimeUtc((Join-Path (Split-Path -Parent $File) 'desktop-ready.flag')) -ge $Requested){return $true}
  }
  Start-Sleep -Milliseconds 400
 }while([DateTime]::UtcNow -lt $until)
 return $false
}
function Get-UpdateBackupFiles([string]$Root,[string]$Skin,$SkinOwner){
 $inventory=Read-UpdateJson (Resolve-OwnedUpdatePath $Root 'update-inventory.json') 262144
 if(!$inventory -or $inventory.format -ne 1 -or !$inventory.files -or @($inventory.files).Count -gt 2048){throw 'Safe rollback is unavailable for this installation.'}
 foreach($name in $inventory.files){
  if($name -match '(?i)(^|[\\/])(runtime-data|photo-cards|dock-icons|music-data|artifacts|outputs)([\\/]|$)|(^|[\\/])(devices\.bin|pins\.json|theme\.txt|paused\.txt|wallpaper[^\\/]*\.png)$|\.(pfx|p12|pem|key)$'){throw 'Unexpected personal data path in update inventory.'}
  Resolve-OwnedUpdatePath $Root $name
 }
 foreach($name in 'update-inventory.json','prismglass-install.json','installer-runtime.ini'){Resolve-OwnedUpdatePath $Root $name}
 foreach($file in Get-ChildItem -LiteralPath $Root -File -Filter 'unins*'){if($file.Name -match '^unins\d+\.(dat|exe|msg)$'){Resolve-OwnedUpdatePath $Root $file.Name}}
 foreach($name in @($SkinOwner.files)){if($name){Resolve-OwnedUpdatePath $Skin $name}}
 Resolve-OwnedUpdatePath $Skin '.prismglass-owner.json'
}
function Remove-FailedUpdateSkins([string]$Root,[string]$Skin,$Before){
 $after=Read-UpdateJson (Resolve-OwnedUpdatePath $Skin '.prismglass-owner.json') 262144
 if(!$after){return}
 if($after.product -cne 'PrismGlass.Desktop' -or $after.root -ine $Root -or @($after.files).Count -gt 2048){throw 'Cannot verify ownership of skins added by the failed installer.'}
 $original=@(foreach($name in @($Before.files)){if($name){Resolve-OwnedUpdatePath $Skin $name}})
 $added=@(foreach($name in @($after.files)){
  if(!$name){continue}
  $path=Resolve-OwnedUpdatePath $Skin $name
  if($path -notin $original -and (Test-Path -LiteralPath $path -PathType Leaf)){if($path -eq (Join-Path $Skin '.prismglass-owner.json')){throw 'Unexpected owner record in skin list.'};$path}
 })
 foreach($path in $added){Remove-Item -LiteralPath $path -Force}
}

$Root=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
$record=Read-UpdateJson (Resolve-OwnedUpdatePath $Root 'prismglass-install.json')
if(!$record -or $record.product -cne 'PrismGlass.Desktop' -or $record.root -ine $Root -or $record.version -ne 1){throw 'Automatic updates require an installer-managed PrismGlass installation.'}
$app=Join-Path $Root 'LiquidDesktop'
$exe=Resolve-OwnedUpdatePath $app 'LiquidDesktop.exe'
$launcher=Resolve-OwnedUpdatePath $app 'PrismGlass.exe'
$updateDir=Resolve-OwnedUpdatePath $app 'runtime-data/updates'
$resultFile=Resolve-OwnedUpdatePath $updateDir 'apply-result.json'
$script:updateSession=[Diagnostics.Process]::GetCurrentProcess().SessionId
$current=(Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
$version='';$status='failed';$message='自动更新未完成。';$desktopStopped=$false;$companionStopped=$false;$companionWasRunning=$false;$installStarted=$false;$restoreFailed=$false
$backup=$null;$restore=@();$retainBackup=$false;$payload=$null;$downloadLock=$null;$modeGate=$null;$modeHeld=$false;$skipResult=$false
$gate=[Threading.Mutex]::new($false,'Local\PrismGlassApplyUpdate');$held=$false
try{
 try{$held=$gate.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
 if(!$held){return}
 if(!(Test-Path -LiteralPath (Resolve-OwnedUpdatePath $updateDir 'enabled') -PathType Leaf)){return}
 $deadline=[DateTime]::UtcNow.AddSeconds(3)
 do{try{$downloadLock=[IO.File]::Open((Resolve-OwnedUpdatePath $updateDir 'download.lock'),'OpenOrCreate','ReadWrite','None')}catch [IO.IOException]{Start-Sleep -Milliseconds 80}}while(!$downloadLock -and [DateTime]::UtcNow -lt $deadline)
 if(!$downloadLock){return}
 $stage=Read-UpdateJson (Resolve-OwnedUpdatePath $updateDir 'staged.json')
 $version=(Get-UpdateCandidate $stage $current).ToString(3)
 $result=Read-UpdateJson $resultFile
 if($result.version -ceq $version -and $result.status -eq 'failed'){$skipResult=$true;return}
 $desktop=Read-ActiveUpdateOwner (Join-Path $app 'desktop-host.json') $exe
 $companion=Read-ActiveUpdateOwner (Join-Path $app 'companion-host.json') $exe
 if(!(Test-UpdateCanStop $desktop $companion)){$status='deferred';$message='伴随屏或桌面正在使用。空闲后会再次尝试更新。';return}
 $knownPids=@($desktop.pid,$companion.pid)
 if(@(Get-Process LiquidDesktop,PrismGlass -ErrorAction SilentlyContinue | Where-Object {$_.Path -in @($exe,$launcher) -and $_.Id -notin $knownPids}).Count){$status='deferred';$message='PrismGlass 还有其他窗口或启动操作，稍后再更新。';return}
 try{$official=Read-OfficialUpdateRelease}
 catch{$status='deferred';$message='暂时无法核对 GitHub 发布，稍后再尝试。';return}
 if(!(Test-OfficialUpdateAsset $stage $official)){throw 'GitHub 当前发布与已下载的安装器不一致。'}
 $installer=Resolve-OwnedUpdatePath $updateDir $stage.name
 $payload=[IO.File]::Open($installer,'Open','Read','Read')
 $hasher=[Security.Cryptography.SHA256]::Create()
 try{$digest=[BitConverter]::ToString($hasher.ComputeHash($payload)).Replace('-','').ToLowerInvariant()}finally{$hasher.Dispose()}
 if($payload.Length -ne $stage.size -or $digest -cne $stage.sha256){throw '安装包校验失败。'}
 $skin=[IO.Path]::GetFullPath($record.skin).TrimEnd('\','/')
 $skinOwner=Read-UpdateJson (Resolve-OwnedUpdatePath $skin '.prismglass-owner.json') 262144
 if(!$skinOwner -or $skinOwner.product -cne 'PrismGlass.Desktop' -or $skinOwner.root -ine $Root){throw 'Rainmeter 皮肤归属检查失败。'}
 $paths=@(Get-UpdateBackupFiles $Root $skin $skinOwner|Sort-Object -Unique)
 $backup=Resolve-OwnedUpdatePath $updateDir ('rollback-'+[guid]::NewGuid().ToString('N'))
 [IO.Directory]::CreateDirectory($backup)|Out-Null
 $i=0
 foreach($file in $paths){if(Test-Path -LiteralPath $file -PathType Leaf){$copy=Join-Path $backup ([string]$i++);[IO.File]::Copy($file,$copy);$restore+=@([pscustomobject]@{file=$file;copy=$copy})}}
 if(@($restore|Where-Object file -eq $exe).Count -ne 1){throw '无法备份当前程序，未开始更新。'}
 [IO.File]::WriteAllText((Join-Path $backup 'restore.json'),($restore|ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
 if(!(Test-Path -LiteralPath (Resolve-OwnedUpdatePath $updateDir 'enabled') -PathType Leaf)){$skipResult=$true;return}
 $latestCompanion=Read-ActiveUpdateOwner (Join-Path $app 'companion-host.json') $exe
 $latestDesktop=Read-ActiveUpdateOwner (Join-Path $app 'desktop-host.json') $exe
 if(!(Test-UpdateCanStop $latestDesktop $latestCompanion) -or $latestCompanion.pid -ne $companion.pid -or $latestCompanion.startTicks -cne $companion.startTicks -or $latestDesktop.pid -ne $desktop.pid -or $latestDesktop.startTicks -cne $desktop.startTicks){$status='deferred';$message='检测到桌面或伴随屏状态变化，已保留下载并等待空闲。';return}
 if($companion){
  $owner=Get-Process -Id $companion.pid -ErrorAction SilentlyContinue
  try{
   if($owner){
    [void](Invoke-UpdateWorker $exe @('--update-stop-companion') 5000)
    if(!$owner.WaitForExit(5500)){
     $request=Resolve-OwnedUpdatePath $app 'companion-update-stop.request'
     [IO.File]::Delete($request)
     $stopping=Read-ActiveUpdateOwner (Join-Path $app 'companion-host.json') $exe
     if($stopping.state -eq 'stopping'){$owner.WaitForExit()}
     elseif(!$owner.HasExited){$status='deferred';$message='伴随屏仍在使用，空闲后再尝试。';return}
    }
   }
   $companionStopped=$true;$companionWasRunning=$companion.companionRunning -eq $true
  }finally{if($owner){$owner.Dispose()}}
 }
 if($desktop){
  $desktopStopped=$true
  $shell=Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell/v1.0/powershell.exe'
  $modeScript=Resolve-OwnedUpdatePath $Root 'DesktopMode.ps1'
  if((Invoke-UpdateWorker $shell @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"'+$modeScript+'"'),'-Mode','Game') 60000) -ne 0){throw '旧版桌面未能正常退出。'}
 }
 $modeGate=[Threading.Mutex]::new($false,'Local\PrismGlassDesktopMode')
 try{$modeHeld=$modeGate.WaitOne(10000)}catch [Threading.AbandonedMutexException]{$modeHeld=$true}
 if(!$modeHeld){throw '桌面正在执行其他操作，尚未开始安装。'}
 if(@(Get-Process LiquidDesktop,PrismGlass -ErrorAction SilentlyContinue|Where-Object {$_.Path -in @($exe,$launcher)}).Count){throw 'PrismGlass 仍有窗口或后台组件在运行。'}
 $args=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$Root+'"'),('/RAINMETER="'+$record.rainmeter+'"'),('/POWERSHELL="'+$record.powershell+'"'))
 $installStarted=$true
 # Inno owns its install transaction. Wait for it before attempting our restoration.
 $setup=Start-Process -FilePath $installer -ArgumentList $args -WindowStyle Hidden -PassThru
 try{[void]$setup.WaitForExit();$exitCode=$setup.ExitCode}finally{$setup.Dispose()}
 $installed=Read-UpdateJson (Resolve-OwnedUpdatePath $Root 'prismglass-install.json')
 if($exitCode -ne 0 -or !$installed -or $installed.root -ine $Root -or !$installed.buildId.StartsWith($version+'+',[StringComparison]::Ordinal) -or (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -cne $installed.buildId){throw '安装未完成，正在恢复更新前的程序。'}
 $status='succeeded';$message='已安装 PrismGlass '+$version+'。'
}catch{
 $message=$_.Exception.Message
 if($installStarted){
  try{Remove-FailedUpdateSkins $Root $skin $skinOwner}catch{$restoreFailed=$true}
  foreach($saved in $restore){
   try{
    $dir=Split-Path -Parent $saved.file
    [void][IO.Directory]::CreateDirectory($dir)
    [IO.File]::Copy($saved.copy,$saved.file,$true)
   }catch{$restoreFailed=$true}
  }
  if($restoreFailed){$retainBackup=$true;$message+=' 旧版恢复未完成，本机恢复文件已保留。'}
 }
}finally{
 if($payload){$payload.Dispose()}
 if($status -eq 'succeeded' -and $downloadLock){
  foreach($file in $installer,(Join-Path $updateDir 'staged.json')){Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue}
 }
 if($modeHeld){$modeGate.ReleaseMutex()};if($modeGate){$modeGate.Dispose()}
 if($held){$gate.ReleaseMutex()};$gate.Dispose()
 if(!$restoreFailed){
  try{
   $reopened=[DateTime]::UtcNow
   if($companionStopped){
    if($companionWasRunning){Start-Process -FilePath $exe -ArgumentList '--ensure-companion' -WindowStyle Hidden}
    else{Start-Process -FilePath $exe -ArgumentList '--companion'}
    if(!(Wait-UpdateReopen (Join-Path $app 'companion-host.json') $exe $true $reopened $companionWasRunning)){$message+=' iPad 伴随屏未能恢复，请从开始菜单打开伴随屏管理。'}
   }
   if($desktopStopped){Start-Process -FilePath $launcher}
   if($desktopStopped -and !(Wait-UpdateReopen (Join-Path $app 'desktop-host.json') $exe $false $reopened)){$message+=' 桌面未确认就绪，请从快捷方式重新开启。'}
  }catch{$message+=' 无法重新开启更新前的组件。'}
 }
 if($backup -and !$retainBackup -and (Test-Path -LiteralPath $backup)){
  $safeBackup=(Resolve-Path -LiteralPath $backup).Path
  if($safeBackup -eq $backup -and (Split-Path -Parent $safeBackup) -eq $updateDir -and (Split-Path -Leaf $safeBackup) -match '^rollback-[0-9a-f]{32}$' -and !((Get-Item -LiteralPath $safeBackup -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -and !@(Get-ChildItem -LiteralPath $safeBackup -Force|Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $_.PSIsContainer}).Count){Remove-Item -LiteralPath $safeBackup -Recurse -Force -ErrorAction SilentlyContinue}
 }
 if($version -and $downloadLock -and !$skipResult){
  $details=[ordered]@{version=$version;status=$status;message=$message;atUtc=[DateTime]::UtcNow.ToString('o')}
  $temp=Resolve-OwnedUpdatePath $updateDir ('result-'+[guid]::NewGuid().ToString('N')+'.tmp')
  [IO.File]::WriteAllText($temp,($details|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
  try{if([IO.File]::Exists($resultFile)){[IO.File]::Replace($temp,$resultFile,[NullString]::Value)}else{[IO.File]::Move($temp,$resultFile)}}finally{$downloadLock.Dispose()}
 }elseif($downloadLock){$downloadLock.Dispose()}
}
