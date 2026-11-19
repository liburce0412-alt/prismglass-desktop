param(
 [ValidateSet('Preflight','Configure','Uninstall')][string]$Mode='Preflight',
 [Parameter(Mandatory=$true)][string]$Root,
 [string]$RainmeterPath,
 [string]$PowerShellPath,
 [string]$RainmeterIni,
 [string]$SkinManifest,
 [double]$Latitude=51.5,
 [double]$Longitude=-0.12
)
$ErrorActionPreference='Stop'

function Resolve-SetupPath([string]$Path) {
 if($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path -match '[\r\n\x00]'){throw 'Use a local absolute path.'}
 $full=[IO.Path]::GetFullPath($Path).TrimEnd('\','/')
 if($full.Length -le 3){throw 'A drive root cannot be an installation folder.'}
 $cursor=$full
 while($cursor){if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw ('Linked directories are not accepted: '+$cursor)};$cursor=[IO.Path]::GetDirectoryName($cursor)}
 return $full
}
function Read-SetupRecord([string]$Path) {
 if(!(Test-Path -LiteralPath $Path -PathType Leaf)){return $null}
 if((Get-Item -LiteralPath $Path).Length -gt 262144){throw 'Installation record exceeds its size limit.'}
 try{return Get-Content -LiteralPath $Path -Encoding UTF8 -Raw | ConvertFrom-Json -ErrorAction Stop}catch{throw 'Installation record is invalid; existing files were not replaced.'}
}
function Read-SetupIni([string]$Path,[string]$Section,[string]$Key) {
 if(!(Test-Path -LiteralPath $Path -PathType Leaf)){return ''}
 $current=''
 foreach($raw in [IO.File]::ReadAllLines($Path)){$line=$raw.Trim();if($line -match '^\[([^]]+)\]$'){$current=$Matches[1];continue};$at=$line.IndexOf('=');if($at -gt 0 -and $current -eq $Section -and $line.Substring(0,$at).Trim() -eq $Key){return $line.Substring($at+1).Trim()}}
 return ''
}
function Resolve-SetupSkin([string]$Executable,[string]$Settings) {
 if(!$Settings){$portable=Join-Path (Split-Path $Executable) 'Rainmeter.ini';$Settings=if(Test-Path -LiteralPath $portable){$portable}else{Join-Path $env:APPDATA 'Rainmeter/Rainmeter.ini'}}
 $value=Read-SetupIni $Settings 'Rainmeter' 'SkinPath'
 $base=if($value){[Environment]::ExpandEnvironmentVariables($value)}else{Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter/Skins'}
 return Resolve-SetupPath (Join-Path $base 'PrismGlass')
}
function Assert-SetupOwnership([string]$InstallRoot,[string]$SkinRoot,[bool]$BeforeCopy) {
 $record=Read-SetupRecord (Join-Path $InstallRoot 'prismglass-install.json')
 if($record){
  if($record.product -ne 'PrismGlass.Desktop' -or $record.version -ne 1 -or (Resolve-SetupPath $record.root) -ne $InstallRoot -or (Resolve-SetupPath $record.skin) -ne $SkinRoot){throw 'This installation belongs to a different path or skin directory. Existing files were retained.'}
 }elseif($BeforeCopy -and (Test-Path -LiteralPath $InstallRoot) -and @(Get-ChildItem -LiteralPath $InstallRoot -Force).Count){throw 'This folder contains an older/manual installation or other files. It was not overwritten. Use a new empty folder; do not delete your old data.'}
 if(Test-Path -LiteralPath $SkinRoot){
  $owner=Read-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json')
  if(!$record -or !$owner -or $owner.product -ne 'PrismGlass.Desktop' -or (Resolve-SetupPath $owner.root) -ne $InstallRoot){throw 'A PrismGlass Rainmeter skin already exists and is not owned by this installer. Exit or migrate the old installation before installing; no skin was overwritten.'}
 }
 return $record
}
function Assert-SetupStopped([string]$InstallRoot) {
 $exe=Join-Path $InstallRoot 'LiquidDesktop/LiquidDesktop.exe'
 $running=@(Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe})
 if($running.Count){throw 'Exit the PrismGlass desktop and stop the iPad companion from their tray menus before installing or upgrading.'}
}
function Write-SetupRecord([string]$Path,$Value) {
 $temp=$Path+'.new'
 try{[IO.File]::WriteAllText($temp,($Value | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));Move-Item -LiteralPath $temp -Destination $Path -Force}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
function Remove-SetupLegacyDesktopShortcuts([string]$InstallRoot,[string]$DesktopRoot) {
 $command='-File "'+(Join-Path $InstallRoot 'DesktopMode.ps1')+'"'
 foreach($name in @('开启 PrismGlass.lnk','退出 PrismGlass.lnk')){
  $path=Join-Path $DesktopRoot $name
  if(!(Test-Path -LiteralPath $path -PathType Leaf)){continue}
  # WScript's shortcut filenames depend on the system code page; Shell preserves Unicode paths.
  $shell=New-Object -ComObject Shell.Application
  $folder=$shell.Namespace($DesktopRoot)
  if(!$folder){continue};$item=$folder.ParseName($name)
  if(!$item -or !$item.IsLink){continue};$shortcut=$item.GetLink()
  $launcher=Join-Path $InstallRoot 'LiquidDesktop/PrismGlass.exe'
  if(($shortcut.Path -eq $launcher -and $shortcut.Arguments -in '','--exit') -or ([IO.Path]::GetFileName($shortcut.Path) -in @('pwsh.exe','powershell.exe') -and $shortcut.Arguments.IndexOf($command,[StringComparison]::OrdinalIgnoreCase) -ge 0)){Remove-Item -LiteralPath $path}
 }
}
function Remove-SetupLoginStartup([string]$InstallRoot,[string]$StartupRoot,[switch]$Migrate) {
 $path=Join-Path $StartupRoot 'PrismGlass.lnk'
 if(!(Test-Path -LiteralPath $path -PathType Leaf)){return}
 $shell=New-Object -ComObject Shell.Application
 $folder=$shell.Namespace($StartupRoot)
 if(!$folder){throw 'Cannot read the Windows startup folder.'}
 $item=$folder.ParseName('PrismGlass.lnk')
 if(!$item -or !$item.IsLink){return};$link=$item.GetLink()
 $target=Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell/v1.0/powershell.exe'
 $arguments='-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+(Join-Path $InstallRoot 'DesktopMode.ps1')+'" -Mode Beauty'
 $launcher=Join-Path $InstallRoot 'LiquidDesktop/PrismGlass.exe'
 $legacy=$link.Path -eq $target -and $link.Arguments -eq $arguments
 if($Migrate){
  if($legacy){$link.Path=$launcher;$link.Arguments='--login';$link.ShowCommand=1;$link.Save()}
 }elseif($legacy -or ($link.Path -eq $launcher -and $link.Arguments -eq '--login')){Remove-Item -LiteralPath $path}
}
function Assert-SetupSkinTargets([string]$SkinRoot,[string[]]$RelativePaths,$Previous) {
 foreach($relative in $RelativePaths){
  if([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative)){throw 'Invalid package skin path.'}
  $target=Resolve-SetupPath (Join-Path $SkinRoot $relative)
  if(!$target.StartsWith($SkinRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Skin file escaped its installation directory.'}
  if((Test-Path -LiteralPath $target) -and ($relative -notin $Previous.files -or !(Test-Path -LiteralPath $target -PathType Leaf))){throw ('已有同名自定义皮肤文件，已保留：'+$target)}
 }
}
function Install-SetupSkin([string]$InstallRoot,[string]$SkinRoot,[string]$Pwsh,[double]$Lat,[double]$Lon) {
 $source=Join-Path $InstallRoot 'rainmeter/PrismGlass'
 if(!(Test-Path -LiteralPath (Join-Path $source 'Dock/Main.ini'))){throw 'The installation package is missing its Dock skin.'}
 $files=@(Get-ChildItem -LiteralPath $source -File -Recurse)
 $previous=Read-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json')
 $relativePaths=@($files | ForEach-Object {$_.FullName.Substring($source.TrimEnd('\','/').Length+1)})
 Assert-SetupSkinTargets $SkinRoot $relativePaths $previous
 $copied=[Collections.Generic.List[string]]::new()
 if($previous){foreach($relative in $previous.files){$copied.Add([string]$relative)}}
 $retiredFiles=@('Topbar\Main.ini','Starlight\Main.ini','MoodNote\Main.ini','@Resources\AmbientArt\starlight-nebula.png','@Resources\AmbientArt\sunlit-garden.png','@Resources\DockIcons\README.md','@Resources\WidgetArt\hourglass-shell.png','@Resources\WidgetArt\bonsai.png')
 foreach($name in @('workspace','trash-empty','trash-full')){foreach($frame in 0..11){$retiredFiles+='@Resources\DockIcons\{0}-{1:00}.png' -f $name,$frame}}
 foreach($retired in $retiredFiles){
 if($previous -and $previous.product -eq 'PrismGlass.Desktop' -and (Resolve-SetupPath $previous.root) -eq $InstallRoot -and $copied.Contains($retired) -and !(Test-Path -LiteralPath (Join-Path $source $retired))){
  $old=Resolve-SetupPath (Join-Path $SkinRoot $retired)
  if(!$old.StartsWith($SkinRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Retired skin escaped its installation directory.'}
  if(Test-Path -LiteralPath $old){Remove-Item -LiteralPath $old}
  $folder=Split-Path $old;if((Test-Path -LiteralPath $folder) -and !@(Get-ChildItem -LiteralPath $folder -Force).Count){Remove-Item -LiteralPath $folder}
  [void]$copied.Remove($retired)
 }
 }
 $owner=[ordered]@{product='PrismGlass.Desktop';root=$InstallRoot;files=$copied.ToArray()}
 New-Item -ItemType Directory -Path $SkinRoot -Force | Out-Null
 # Record ownership before copying so an interrupted install remains repairable.
 Write-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json') $owner
 foreach($file in $files){
  $relative=$file.FullName.Substring($source.TrimEnd('\','/').Length+1)
  $target=Resolve-SetupPath (Join-Path $SkinRoot $relative)
  if($relative -notin $copied){
   Assert-SetupSkinTargets $SkinRoot @($relative) $previous
   $copied.Add($relative);$owner.files=$copied.ToArray();Write-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json') $owner
  }
  New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
  if($file.Extension -in '.ini','.inc','.lua'){
   $content=[IO.File]::ReadAllText($file.FullName).Replace('{{INSTALL_ROOT}}',$InstallRoot).Replace('{{POWERSHELL}}',$Pwsh).Replace('{{LATITUDE}}',$Lat.ToString([Globalization.CultureInfo]::InvariantCulture)).Replace('{{LONGITUDE}}',$Lon.ToString([Globalization.CultureInfo]::InvariantCulture))
   [IO.File]::WriteAllText($target,$content,[Text.Encoding]::Unicode)
  }else{Copy-Item -LiteralPath $file.FullName -Destination $target -Force}
 }
}
function Remove-SetupSkin([string]$InstallRoot,[string]$SkinRoot) {
 if(!(Test-Path -LiteralPath $SkinRoot)){return}
 $ownerPath=Join-Path $SkinRoot '.prismglass-owner.json'
 $owner=Read-SetupRecord $ownerPath
 if(!$owner -or $owner.product -ne 'PrismGlass.Desktop' -or (Resolve-SetupPath $owner.root) -ne $InstallRoot){throw 'Skin ownership could not be verified; nothing was deleted.'}
 $targets=[Collections.Generic.List[string]]::new()
 foreach($relative in $owner.files){
  if([IO.Path]::IsPathRooted($relative)){throw 'Invalid installed skin file manifest.'}
  $path=Resolve-SetupPath (Join-Path $SkinRoot $relative)
  if(!$path.StartsWith($SkinRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Skin manifest escaped its directory.'}
  $targets.Add($path)
 }
 $directories=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($path in $targets){if(Test-Path -LiteralPath $path -PathType Leaf){Remove-Item -LiteralPath $path};$directory=Split-Path $path;while($directory -ne $SkinRoot -and $directory.StartsWith($SkinRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){[void]$directories.Add($directory);$directory=Split-Path $directory}}
 foreach($folder in Get-ChildItem -LiteralPath $SkinRoot -Directory -Filter 'Dock-monitor-*'){
  $file=Join-Path $folder.FullName 'Main.ini'
  if($folder.Name -match '^Dock-monitor-[0-9]+$' -and (Test-Path -LiteralPath $file) -and (Get-Content -LiteralPath $file -TotalCount 1) -eq '; PrismGlass generated monitor instance'){
   [void](Resolve-SetupPath $file);Remove-Item -LiteralPath $file;[void]$directories.Add($folder.FullName)
  }
 }
 Remove-Item -LiteralPath $ownerPath
 foreach($directory in @($directories | Sort-Object Length -Descending)){if((Test-Path -LiteralPath $directory -PathType Container) -and !@(Get-ChildItem -LiteralPath $directory -Force).Count){Remove-Item -LiteralPath $directory}}
 if(!@(Get-ChildItem -LiteralPath $SkinRoot -Force).Count){Remove-Item -LiteralPath $SkinRoot}
 else{$owner.files=@();Write-SetupRecord $ownerPath $owner}
}
function Stop-SetupRainmeterSkins([string]$InstallRoot,[string]$SkinRoot,[string]$Executable,[int]$TimeoutMilliseconds=3000) {
 if(!(Test-Path -LiteralPath $SkinRoot)){return}
 $processes=@(Get-Process Rainmeter -ErrorAction SilentlyContinue);$running=$false
 try{foreach($process in $processes){
  $path=$process.Path
  if(!$path){throw 'Cannot verify the Rainmeter process path. Skin files were retained; check process permissions.'}
  if($path -eq $Executable){$running=$true}
 }}finally{foreach($process in $processes){$process.Dispose()}}
 if(!$running){return}
 $owner=Read-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json')
 if(!$owner -or $owner.product -ne 'PrismGlass.Desktop' -or (Resolve-SetupPath $owner.root) -ne $InstallRoot){throw 'Skin ownership could not be verified; no deactivation commands were sent.'}
 if(!('RainmeterCommands' -as [type])){Add-Type -Path (Join-Path $InstallRoot 'LiquidDesktop/RainmeterCommands.cs')}
 if(![RainmeterCommands]::Probe($Executable,50)){throw 'Rainmeter did not respond. Skin files were retained; restore Rainmeter before uninstalling.'}
 $pending=[Collections.Generic.List[string]]::new()
 foreach($folder in Get-ChildItem -LiteralPath $SkinRoot -Directory){
  $file=Join-Path $folder.FullName 'Main.ini'
  $relative=$folder.Name+'\Main.ini'
  $generated=$folder.Name -match '^Dock-monitor-[0-9]+$' -and (Test-Path -LiteralPath $file) -and (Get-Content -LiteralPath $file -TotalCount 1) -eq '; PrismGlass generated monitor instance'
  if($relative -in $owner.files -or $generated){[RainmeterCommands]::Send($Executable,('!DeactivateConfig "PrismGlass\'+$folder.Name+'"'),250);$pending.Add($file)}
 }
 $wait=[Diagnostics.Stopwatch]::StartNew()
 do{
  $open=@($pending | Where-Object {[RainmeterCommands]::SkinOpen($_)})
  if(!$open.Count){return}
  if($wait.ElapsedMilliseconds -ge $TimeoutMilliseconds){break}
  Start-Sleep -Milliseconds 100
 }while($true)
 throw 'Rainmeter has not closed the PrismGlass skins. Files were retained; restore Rainmeter before uninstalling.'
}

$Root=Resolve-SetupPath $Root
$record=Read-SetupRecord (Join-Path $Root 'prismglass-install.json')
if($Mode -eq 'Uninstall'){
 if(!$record -or $record.product -ne 'PrismGlass.Desktop' -or (Resolve-SetupPath $record.root) -ne $Root){throw 'Installer ownership is missing. Files were retained.'}
 $skin=Resolve-SetupPath $record.skin
 [void](Assert-SetupOwnership $Root $skin $false)
 $exe=Join-Path $Root 'LiquidDesktop/LiquidDesktop.exe'
 if(@(Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe}).Count){
  $desktopMode=Join-Path $Root 'DesktopMode.ps1'
  $argument=@{Mode='Game'}
  if((Get-Command -Name $desktopMode).Parameters.ContainsKey('StopCompanion')){$argument.StopCompanion=$true}
  & $desktopMode @argument
 }
 Assert-SetupStopped $Root
 Stop-SetupRainmeterSkins $Root $skin $record.rainmeter
 Remove-SetupLoginStartup $Root ([Environment]::GetFolderPath('Startup'))
 Remove-SetupSkin $Root $skin
 # User data and companion system configuration intentionally remain available.
 'PrismGlass skin removed. User data, companion setup records and shared runtimes were retained.'
 return
}
if(!(Test-Path -LiteralPath $RainmeterPath -PathType Leaf) -or [IO.Path]::GetFileName($RainmeterPath) -ne 'Rainmeter.exe'){throw 'Select the installed Rainmeter.exe.'}
if(!(Test-Path -LiteralPath $PowerShellPath -PathType Leaf) -or [IO.Path]::GetFileName($PowerShellPath) -notin @('pwsh.exe','powershell.exe')){throw '请选择 Windows PowerShell 5.1 或 PowerShell 7 程序。'}
$RainmeterPath=Resolve-SetupPath $RainmeterPath;$PowerShellPath=Resolve-SetupPath $PowerShellPath
$skin=Resolve-SetupSkin $RainmeterPath $RainmeterIni
[void](Assert-SetupOwnership $Root $skin ($Mode -eq 'Preflight'))
Assert-SetupStopped $Root
if($Latitude -lt -90 -or $Latitude -gt 90 -or $Longitude -lt -180 -or $Longitude -gt 180 -or [double]::IsNaN($Latitude) -or [double]::IsNaN($Longitude)){throw 'Weather coordinates are invalid.'}
if($Mode -eq 'Preflight'){
 if($SkinManifest){
  $relativePaths=[IO.File]::ReadAllLines($SkinManifest)
  try{Assert-SetupSkinTargets $skin $relativePaths (Read-SetupRecord (Join-Path $skin '.prismglass-owner.json'))}
  catch{[IO.File]::WriteAllText($SkinManifest+'.error',$_.Exception.Message,[Text.UTF8Encoding]::new($false));throw}
 }
 'Installation preflight passed; no files or desktop settings changed.';return
}
$app=Join-Path $Root 'LiquidDesktop'
if(!(Test-Path -LiteralPath (Join-Path $app 'LiquidDesktop.exe'))){throw 'The application executable is missing.'}
if($record){$Latitude=$record.latitude;$Longitude=$record.longitude}
$record=[ordered]@{product='PrismGlass.Desktop';version=1;root=$Root;skin=$skin;rainmeter=$RainmeterPath;powershell=$PowerShellPath;latitude=$Latitude;longitude=$Longitude}
$buildInfo=Join-Path $Root 'build-info.json'
if(Test-Path -LiteralPath $buildInfo){$record.buildId=([IO.File]::ReadAllText($buildInfo)|ConvertFrom-Json).buildId}
Write-SetupRecord (Join-Path $Root 'prismglass-install.json') $record
foreach($config in Get-ChildItem -LiteralPath (Join-Path $Root 'rainmeter/PrismGlass') -Directory){
 if((Test-Path -LiteralPath (Join-Path $config.FullName 'Main.ini')) -and !(Test-Path -LiteralPath (Join-Path $skin ($config.Name+'/Main.ini')))){
  [IO.File]::WriteAllText((Join-Path $app 'skin-catalog-pending.flag'),'refresh before first activation');break
 }
}
Install-SetupSkin $Root $skin $PowerShellPath $Latitude $Longitude
[IO.File]::WriteAllText((Join-Path $app 'rainmeter-path.txt'),$RainmeterPath)
[IO.File]::WriteAllText((Join-Path $app 'skin-path.txt'),$skin)
Remove-SetupLegacyDesktopShortcuts $Root ([Environment]::GetFolderPath('Desktop'))
Remove-SetupLoginStartup $Root ([Environment]::GetFolderPath('Startup')) -Migrate
if(!(Test-Path -LiteralPath (Join-Path $app 'pins.json'))){
 @(@{id='explorer';name='文件';path=(Join-Path $env:WINDIR 'explorer.exe');arguments='';icon=''},@{id='notepad';name='记事本';path=(Join-Path $env:WINDIR 'System32/notepad.exe');arguments='';icon=''}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $app 'pins.json') -Encoding utf8
}
'Installed. Start PrismGlass manually; login startup, wallpaper, desktop state and network settings were not changed.'
