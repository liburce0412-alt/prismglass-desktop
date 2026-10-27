param(
 [string]$Destination=(Join-Path $env:LOCALAPPDATA 'PrismGlass'),
 [string]$RainmeterPath=(Join-Path $env:ProgramFiles 'Rainmeter/Rainmeter.exe'),
 [string]$RainmeterIni,
 [double]$Latitude=51.5,[double]$Longitude=-0.12,
 [switch]$EnableMusicBridge,
 [switch]$PrepareOnly
)
$ErrorActionPreference='Stop'
$bundle=$PSScriptRoot
if(!(Test-Path -LiteralPath "$bundle/LiquidDesktop/LiquidDesktop.exe")){throw 'Run this script from dist/PrismGlass after Build.ps1.'}
if(!(Test-Path -LiteralPath $RainmeterPath -PathType Leaf)){throw 'Install Rainmeter first, or provide -RainmeterPath.'}
$pwsh=(Get-Command pwsh.exe -ErrorAction Stop).Source
if($Latitude -lt -90 -or $Latitude -gt 90 -or $Longitude -lt -180 -or $Longitude -gt 180){throw 'Invalid coordinates'}
$Destination=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $Destination){throw 'Destination exists. Choose a fresh directory; existing user data is not overwritten.'}
if(!$PrepareOnly){& (Join-Path $bundle 'SetupIntegration.ps1') -Mode Preflight -Root $Destination -RainmeterPath $RainmeterPath -PowerShellPath $pwsh -RainmeterIni $RainmeterIni -Latitude $Latitude -Longitude $Longitude}
New-Item -ItemType Directory -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $bundle | Copy-Item -Destination $Destination -Recurse
$RainmeterPath=[IO.Path]::GetFullPath($RainmeterPath)
[IO.File]::WriteAllText("$Destination/LiquidDesktop/rainmeter-path.txt",$RainmeterPath)
if($EnableMusicBridge){New-Item -ItemType File "$Destination/music-bridge.enabled" | Out-Null}
foreach($file in Get-ChildItem "$Destination/rainmeter/PrismGlass" -File -Recurse){
 $text=[IO.File]::ReadAllText($file.FullName)
 $text=$text.Replace('{{INSTALL_ROOT}}',$Destination).Replace('{{POWERSHELL}}',$pwsh).Replace('{{LATITUDE}}',$Latitude.ToString([Globalization.CultureInfo]::InvariantCulture)).Replace('{{LONGITUDE}}',$Longitude.ToString([Globalization.CultureInfo]::InvariantCulture))
 # Rainmeter uses Windows INI parsing: a UTF-8 BOM hides the first section.
 # Lua also needs UTF-16 to enable Rainmeter's Unicode string conversion.
 $encoding=if($file.Extension -in '.ini','.inc','.lua'){[Text.Encoding]::Unicode}else{[Text.UTF8Encoding]::new($false)}
 [IO.File]::WriteAllText($file.FullName,$text,$encoding)
}
$pins=@(
 @{id='explorer';name='文件';path=(Join-Path $env:WINDIR 'explorer.exe');arguments='';icon=''},
 @{id='notepad';name='记事本';path=(Join-Path $env:WINDIR 'System32/notepad.exe');arguments='';icon=''}
)
$pins | ConvertTo-Json | Set-Content "$Destination/LiquidDesktop/pins.json" -Encoding utf8
if($PrepareOnly){Write-Output "Prepared $Destination; no shell settings, shortcuts or Rainmeter skins changed.";return}
& (Join-Path $Destination 'SetupIntegration.ps1') -Mode Configure -Root $Destination -RainmeterPath $RainmeterPath -PowerShellPath $pwsh -RainmeterIni $RainmeterIni -Latitude $Latitude -Longitude $Longitude
$ws=New-Object -ComObject WScript.Shell
foreach($item in @(@('开启 PrismGlass','Beauty'),@('退出 PrismGlass','Game'))){
 $lnk=$ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) ($item[0]+'.lnk')))
 $lnk.TargetPath=$pwsh;$lnk.Arguments='-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+$Destination+'\DesktopMode.ps1" -Mode '+$item[1];$lnk.WindowStyle=7;$lnk.IconLocation=Join-Path $Destination 'LiquidDesktop/LiquidDesktop.exe';$lnk.Save()
}
Write-Output 'Installed. Start and exit PrismGlass manually with the desktop shortcuts. No login startup item or scheduled task was created.'
