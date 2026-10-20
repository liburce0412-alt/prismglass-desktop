param([string]$RainmeterPath='D:\Rainmeter.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/glass-'+[guid]::NewGuid().ToString('N'))
$skin=Join-Path $fixture 'Skins/GlassTest'
$evidence=Join-Path $repo 'artifacts/tests/dock-glass-checks.txt'
$process=$null
New-Item -ItemType Directory -Force -Path $skin,(Split-Path $evidence) | Out-Null
try{
 @"
[Rainmeter]
SkinPath=$fixture\Skins\
Logging=1
DisableVersionCheck=1
[GlassTest]
Active=1
WindowX=-16000
WindowY=-16000
KeepOnScreen=0
SavePosition=0
AlwaysOnTop=-2
"@ | Set-Content -LiteralPath "$fixture/Rainmeter.ini" -Encoding unicode
 @"
[Rainmeter]
Update=100
[Variables]
LiquidRoot=$fixture\
DockWidth=200
[LiquidState]
Measure=Script
ScriptFile=$repo\rainmeter\PrismGlass\@Resources\LiquidGlass.lua
[Check]
Measure=Script
ScriptFile=$repo\tests\TestDockGlass.lua
[HitArea]
Meter=Shape
Shape=Rectangle 0,0,200,88 | Fill Color 0,0,0,1 | StrokeWidth 0
[Rim]
Group=LegacyGlass
Meter=Shape
DynamicVariables=1
UpdateDivider=-1
Shape=Rectangle 1,1,(#DockWidth#-2),86,24 | Fill Color 223,234,247,220 | StrokeWidth 0
"@ | Set-Content -LiteralPath "$skin/Main.ini" -Encoding unicode
 ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()+' Sky') | Set-Content -LiteralPath "$fixture/heartbeat.txt" -Encoding ascii
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds(15)
 do{
  Start-Sleep -Milliseconds 200
  [string]$text=if(Test-Path -LiteralPath "$fixture/glass-checks.txt"){Get-Content -LiteralPath "$fixture/glass-checks.txt" -Raw}else{''}
 }while($text -notmatch 'Completed' -and [DateTime]::UtcNow -lt $deadline -and !$process.HasExited)
 $text | Set-Content -LiteralPath $evidence -Encoding utf8
 if(Test-Path -LiteralPath "$fixture/Rainmeter.log"){Copy-Item -LiteralPath "$fixture/Rainmeter.log" -Destination (Join-Path $repo 'artifacts/tests/dock-glass-rainmeter.log') -Force}
 $text
 if($text -notmatch 'Completed 5 native glass checks; failures=0.'){throw "Native glass regression failed; see $evidence"}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000) | Out-Null;$process.Dispose()}
 if(Test-Path -LiteralPath $fixture){
  $resolved=(Resolve-Path -LiteralPath $fixture).Path
  $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
  if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
 }
}
