param([string]$RainmeterPath='D:/Rainmeter.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/ambient-lua-'+[guid]::NewGuid().ToString('N'))
$skin=Join-Path $fixture 'Skins/AmbientTest'
$evidence=Join-Path $repo 'artifacts/tests/ambient-lua-checks.txt'
$process=$null
New-Item -ItemType Directory -Force -Path $skin,(Join-Path $fixture 'photo-cards'),(Split-Path $evidence) | Out-Null
try{
 @"
[Rainmeter]
SkinPath=$fixture\Skins\
Logging=1
DisableVersionCheck=1
[AmbientTest]
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
ProductionScript=$repo\rainmeter\PrismGlass\@Resources\Ambient.lua
[Check]
Measure=Script
ScriptFile=$repo\tests\TestAmbientLua.lua
[Invisible]
Meter=Shape
Shape=Rectangle 0,0,1,1 | Fill Color 0,0,0,1 | StrokeWidth 0
"@ | Set-Content -LiteralPath (Join-Path $skin 'Main.ini') -Encoding unicode
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 $deadline=[datetime]::UtcNow.AddSeconds(10)
 do{
  Start-Sleep -Milliseconds 100
  $report=Join-Path $fixture 'lua-checks.txt'
  [string]$text=if(Test-Path -LiteralPath $report){Get-Content -LiteralPath $report -Raw}else{''}
 }while($text -notmatch '(?m)^(Completed |ERROR )' -and [datetime]::UtcNow -lt $deadline -and !$process.HasExited)
 $text | Set-Content -LiteralPath $evidence -Encoding utf8
 if(!$text.Contains('Completed 27 widget Lua checks.') -or $text -match '(?m)^(FAIL |ERROR )'){throw "Widget Lua checks failed. $text"}
 $text
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000)|Out-Null;$process.Dispose()}
 $absolute=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts directory'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
 if(Test-Path -LiteralPath $absolute){throw 'Fixture cleanup failed'}
}
