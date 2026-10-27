param(
 [ValidateSet('Preflight','Configure','Uninstall')][string]$Mode='Preflight',
 [Parameter(Mandatory=$true)][string]$Root,
 [string]$RainmeterPath,
 [string]$PowerShellPath,
 [string]$RainmeterIni,
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
 try{return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -ErrorAction Stop}catch{throw 'Installation record is invalid; existing files were not replaced.'}
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
 if($running.Count){throw 'Exit PrismGlass from its tray menu before installing or upgrading.'}
}
function Write-SetupRecord([string]$Path,$Value) {
 $temp=$Path+'.new'
 try{[IO.File]::WriteAllText($temp,($Value | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));Move-Item -LiteralPath $temp -Destination $Path -Force}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
function Install-SetupSkin([string]$InstallRoot,[string]$SkinRoot,[string]$Pwsh,[double]$Lat,[double]$Lon) {
 $source=Join-Path $InstallRoot 'rainmeter/PrismGlass'
 if(!(Test-Path -LiteralPath (Join-Path $source 'Dock/Main.ini'))){throw 'The installation package is missing its Dock skin.'}
 $files=@(Get-ChildItem -LiteralPath $source -File -Recurse)
 $previous=Read-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json')
 $copied=[Collections.Generic.List[string]]::new()
 if($previous){foreach($relative in $previous.files){$copied.Add([string]$relative)}}
 $owner=[ordered]@{product='PrismGlass.Desktop';root=$InstallRoot;files=$copied.ToArray()}
 New-Item -ItemType Directory -Path $SkinRoot -Force | Out-Null
 # Record ownership before copying so an interrupted install remains repairable.
 Write-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json') $owner
 foreach($file in $files){
  $relative=[IO.Path]::GetRelativePath($source,$file.FullName)
  $target=Resolve-SetupPath (Join-Path $SkinRoot $relative)
  if(!$target.StartsWith($SkinRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Skin file escaped its installation directory.'}
  New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
  if($file.Extension -in '.ini','.inc','.lua'){
   $content=[IO.File]::ReadAllText($file.FullName).Replace('{{INSTALL_ROOT}}',$InstallRoot).Replace('{{POWERSHELL}}',$Pwsh).Replace('{{LATITUDE}}',$Lat.ToString([Globalization.CultureInfo]::InvariantCulture)).Replace('{{LONGITUDE}}',$Lon.ToString([Globalization.CultureInfo]::InvariantCulture))
   [IO.File]::WriteAllText($target,$content,[Text.Encoding]::Unicode)
  }else{Copy-Item -LiteralPath $file.FullName -Destination $target -Force}
  if(!$copied.Contains($relative)){$copied.Add($relative)};$owner.files=$copied.ToArray();Write-SetupRecord (Join-Path $SkinRoot '.prismglass-owner.json') $owner
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

$Root=Resolve-SetupPath $Root
$record=Read-SetupRecord (Join-Path $Root 'prismglass-install.json')
if($Mode -eq 'Uninstall'){
 if(!$record -or $record.product -ne 'PrismGlass.Desktop' -or (Resolve-SetupPath $record.root) -ne $Root){throw 'Installer ownership is missing. Files were retained.'}
 $skin=Resolve-SetupPath $record.skin
 [void](Assert-SetupOwnership $Root $skin $false)
 $exe=Join-Path $Root 'LiquidDesktop/LiquidDesktop.exe'
 if(@(Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe}).Count){& (Join-Path $Root 'DesktopMode.ps1') -Mode Game}
 Assert-SetupStopped $Root
 if((Test-Path -LiteralPath $skin) -and (Get-Process Rainmeter -ErrorAction SilentlyContinue)){
  $owner=Read-SetupRecord (Join-Path $skin '.prismglass-owner.json')
  foreach($folder in Get-ChildItem -LiteralPath $skin -Directory){
   $file=Join-Path $folder.FullName 'Main.ini'
   $relative=$folder.Name+'\Main.ini'
   $generated=$folder.Name -match '^Dock-monitor-[0-9]+$' -and (Test-Path -LiteralPath $file) -and (Get-Content -LiteralPath $file -TotalCount 1) -eq '; PrismGlass generated monitor instance'
   if($relative -in $owner.files -or $generated){& $record.rainmeter '!DeactivateConfig' ('PrismGlass\'+$folder.Name)}
  }
 }
 Remove-SetupSkin $Root $skin
 # User data and companion system configuration intentionally remain available.
 'PrismGlass skin removed. User data, companion setup records and shared runtimes were retained.'
 return
}
if(!(Test-Path -LiteralPath $RainmeterPath -PathType Leaf) -or [IO.Path]::GetFileName($RainmeterPath) -ne 'Rainmeter.exe'){throw 'Select the installed Rainmeter.exe.'}
if(!(Test-Path -LiteralPath $PowerShellPath -PathType Leaf) -or [IO.Path]::GetFileName($PowerShellPath) -ne 'pwsh.exe'){throw 'Select PowerShell 7 pwsh.exe.'}
$RainmeterPath=Resolve-SetupPath $RainmeterPath;$PowerShellPath=Resolve-SetupPath $PowerShellPath
$skin=Resolve-SetupSkin $RainmeterPath $RainmeterIni
[void](Assert-SetupOwnership $Root $skin ($Mode -eq 'Preflight'))
Assert-SetupStopped $Root
if($Latitude -lt -90 -or $Latitude -gt 90 -or $Longitude -lt -180 -or $Longitude -gt 180 -or [double]::IsNaN($Latitude) -or [double]::IsNaN($Longitude)){throw 'Weather coordinates are invalid.'}
if($Mode -eq 'Preflight'){'Installation preflight passed; no files or desktop settings changed.';return}
$app=Join-Path $Root 'LiquidDesktop'
if(!(Test-Path -LiteralPath (Join-Path $app 'LiquidDesktop.exe'))){throw 'The application executable is missing.'}
if($record){$Latitude=$record.latitude;$Longitude=$record.longitude}
$record=[ordered]@{product='PrismGlass.Desktop';version=1;root=$Root;skin=$skin;rainmeter=$RainmeterPath;powershell=$PowerShellPath;latitude=$Latitude;longitude=$Longitude}
Write-SetupRecord (Join-Path $Root 'prismglass-install.json') $record
if(!(Test-Path -LiteralPath (Join-Path $skin 'Dock/Main.ini'))){[IO.File]::WriteAllText((Join-Path $app 'skin-catalog-pending.flag'),'refresh before first activation')}
Install-SetupSkin $Root $skin $PowerShellPath $Latitude $Longitude
[IO.File]::WriteAllText((Join-Path $app 'rainmeter-path.txt'),$RainmeterPath)
[IO.File]::WriteAllText((Join-Path $app 'skin-path.txt'),$skin)
if(!(Test-Path -LiteralPath (Join-Path $app 'pins.json'))){
 @(@{id='explorer';name='文件';path=(Join-Path $env:WINDIR 'explorer.exe');arguments='';icon=''},@{id='notepad';name='记事本';path=(Join-Path $env:WINDIR 'System32/notepad.exe');arguments='';icon=''}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $app 'pins.json') -Encoding utf8
}
'Installed. Start PrismGlass manually; login startup, wallpaper, desktop state and network settings were not changed.'
