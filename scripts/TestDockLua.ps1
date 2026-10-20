param([string]$RainmeterPath='C:\Program Files\Rainmeter\Rainmeter.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/lua-'+[guid]::NewGuid().ToString('N'))
$skin=Join-Path $fixture 'Skins/DockTest'
$evidence=Join-Path $repo 'artifacts/tests/dock-lua-checks.txt'
$process=$null
New-Item -ItemType Directory -Force -Path $skin,(Split-Path $evidence) | Out-Null
try{
 @"
[Rainmeter]
SkinPath=$fixture\Skins\
Logging=1
DisableVersionCheck=1
[DockTest]
Active=1
WindowX=-16000
WindowY=-16000
KeepOnScreen=0
SavePosition=0
AlwaysOnTop=-2
"@ | Set-Content -LiteralPath "$fixture/Rainmeter.ini" -Encoding unicode
 @"
[Rainmeter]
Update=1000
[Variables]
Fixture=$fixture\
ProductionScript=$repo\rainmeter\PrismGlass\@Resources\Dock.lua
[Check]
Measure=Script
ScriptFile=$repo\tests\TestDockLua.lua
[Invisible]
Meter=Shape
Shape=Rectangle 0,0,1,1 | Fill Color 0,0,0,1 | StrokeWidth 0
"@ | Set-Content -LiteralPath "$skin/Main.ini" -Encoding unicode
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 $deadline=[datetime]::UtcNow.AddSeconds(20)
 do{
  Start-Sleep -Milliseconds 200
  [string]$text=if(Test-Path -LiteralPath "$fixture/lua-checks.txt"){Get-Content -LiteralPath "$fixture/lua-checks.txt" -Raw}else{''}
 }while($text -notmatch 'Completed|ERROR' -and [datetime]::UtcNow -lt $deadline -and !$process.HasExited)
 $text | Set-Content -LiteralPath $evidence -Encoding utf8
 if(Test-Path -LiteralPath "$fixture/Rainmeter.log"){Copy-Item -LiteralPath "$fixture/Rainmeter.log" -Destination (Join-Path $repo 'artifacts/tests/dock-lua-rainmeter.log') -Force}
 if($text -notmatch 'Completed 13 native Lua checks.' -or $text -match 'FAIL|ERROR'){throw "Native Lua checks failed. See $evidence and $fixture"}
 $text
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000) | Out-Null;$process.Dispose()}
 # Remove only this generated isolated profile, never the user's Rainmeter profile.
 if(Test-Path -LiteralPath $fixture){
  $resolved=(Resolve-Path -LiteralPath $fixture).Path
  $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
  if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
 }
}
