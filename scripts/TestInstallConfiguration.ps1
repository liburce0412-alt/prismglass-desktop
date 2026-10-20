param([Parameter(Mandatory=$true)][string]$Bundle,[string]$RainmeterPath='D:\Rainmeter.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/install-'+[guid]::NewGuid().ToString('N'))
$destination=Join-Path $fixture '编码回归'
New-Item -ItemType Directory -Path $fixture | Out-Null
try{
 & (Join-Path $Bundle 'Install.ps1') -Destination $destination -RainmeterPath $RainmeterPath -PrepareOnly
 Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class RainmeterIniCheck {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern int GetPrivateProfileInt(string section,string key,int fallback,string file);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern int GetPrivateProfileString(string section,string key,string fallback,StringBuilder text,int size,string file);
}
'@
 $dock=Join-Path $destination 'rainmeter/PrismGlass/Dock/Main.ini'
 if([RainmeterIniCheck]::GetPrivateProfileInt('Rainmeter','Update',1000,$dock) -ne 16){throw 'Installed Dock lost its 16 ms refresh setting'}
 $root=[Text.StringBuilder]::new(2048)
 [void][RainmeterIniCheck]::GetPrivateProfileString('Variables','LiquidRoot','',$root,$root.Capacity,$dock)
 if($root.ToString() -ne "$destination\LiquidDesktop\"){throw 'Native INI reader lost the Chinese installation path'}
 $lua=Join-Path $destination 'rainmeter/PrismGlass/@Resources/Dock.lua'
 if([IO.File]::ReadAllText($lua) -notmatch 'function Update\(\)'){throw 'Lua script was damaged during preparation'}
 $bytes=[IO.File]::ReadAllBytes($lua)
 if($bytes[0] -ne 255 -or $bytes[1] -ne 254){throw 'Installed Lua must use UTF-16 to enable Unicode conversion in Rainmeter'}
 'PASS native Windows INI parser reads the installed 16 ms refresh and Unicode path; Lua uses UTF-16 for Unicode strings.' | Tee-Object -FilePath (Join-Path $repo 'artifacts/tests/install-configuration.txt')
}finally{
 $resolved=(Resolve-Path -LiteralPath $fixture).Path
 $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Installation fixture escaped artifacts'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
 if(Test-Path -LiteralPath $resolved){throw 'Installation fixture cleanup failed'}
}
