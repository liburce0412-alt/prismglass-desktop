param([string]$RainmeterPath='C:\Program Files\Rainmeter\Rainmeter.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/focus-lua-'+[guid]::NewGuid().ToString('N'))
$skin=Join-Path $fixture 'Skins/FocusTest'
$evidence=Join-Path $repo 'artifacts/tests/focus-lua-checks.txt'
$process=$null
if(!(Test-Path -LiteralPath $RainmeterPath -PathType Leaf)){throw 'The existing Rainmeter runtime was not found.'}
New-Item -ItemType Directory -Force -Path $skin,(Split-Path $evidence) | Out-Null
try{
 @"
[Rainmeter]
SkinPath=$fixture\Skins\
Logging=1
DisableVersionCheck=1
[FocusTest]
Active=1
WindowX=-16000
WindowY=-16000
KeepOnScreen=0
SavePosition=0
AlwaysOnTop=-2
"@ | Set-Content -LiteralPath (Join-Path $fixture 'Rainmeter.ini') -Encoding unicode
 @"
[Rainmeter]
Update=1000
[Variables]
Fixture=$fixture\
ProductionScript=$repo\rainmeter\PrismGlass\@Resources\Focus.lua
ProductionWeather=$repo\rainmeter\PrismGlass\@Resources\Weather.lua
ProductionExport=$repo\rainmeter\PrismGlass\@Resources\WidgetExport.lua
[Check]
Measure=Script
ScriptFile=$repo\tests\TestFocusLua.lua
[Invisible]
Meter=Shape
Shape=Rectangle 0,0,1,1 | Fill Color 0,0,0,1 | StrokeWidth 0
"@ | Set-Content -LiteralPath (Join-Path $skin 'Main.ini') -Encoding unicode
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 $deadline=[datetime]::UtcNow.AddSeconds(20)
 do{
  Start-Sleep -Milliseconds 200
  $report=Join-Path $fixture 'lua-checks.txt'
  [string]$text=if(Test-Path -LiteralPath $report){Get-Content -LiteralPath $report -Raw}else{''}
 }while($text -notmatch '(?m)^(Completed |ERROR )' -and [datetime]::UtcNow -lt $deadline -and !$process.HasExited)
 $text | Set-Content -LiteralPath $evidence -Encoding utf8
 if(Test-Path -LiteralPath (Join-Path $fixture 'Rainmeter.log')){Copy-Item -LiteralPath (Join-Path $fixture 'Rainmeter.log') -Destination (Join-Path $repo 'artifacts/tests/focus-lua-rainmeter.log') -Force}
 if(!$text.Contains('Completed 30 native Focus Lua checks.') -or $text -match '(?m)^(FAIL |ERROR )'){throw "Native Focus checks failed. See $evidence"}
 $text
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
