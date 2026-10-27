param([string]$RainmeterPath='C:\Program Files\Rainmeter\Rainmeter.exe',[ValidateRange(1,32)][int]$IconCount=16)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/icon-bridge-'+[guid]::NewGuid().ToString('N'))
$skins=Join-Path $fixture 'Skins/PrismGlass'
$skin=Join-Path $skins 'Dock'
$out=Join-Path $repo 'artifacts/tests'
$process=$null
New-Item -ItemType Directory -Force -Path $skin,(Join-Path $skins '@Resources'),$out | Out-Null
try{
 @"
[Rainmeter]
SkinPath=$fixture\Skins\
Logging=1
DisableVersionCheck=1
[PrismGlass\Dock]
Active=1
WindowX=-16000
WindowY=-16000
KeepOnScreen=0
SavePosition=0
AlwaysOnTop=-2
"@ | Set-Content -LiteralPath (Join-Path $fixture 'Rainmeter.ini') -Encoding unicode
 $main=Get-Content -LiteralPath (Join-Path $repo 'rainmeter/PrismGlass/Dock/Main.ini') -Raw
 $main=$main.Replace('{{INSTALL_ROOT}}\LiquidDesktop\',$fixture+'\').Replace('ScriptFile=#@#Dock.lua','ScriptFile='+$repo+'\tests\TestDockIconBridge.lua')
 $main=$main.Replace('DockMonitorLeft=0','DockMonitorLeft=-16000').Replace('DockMonitorTop=0','DockMonitorTop=-16000').Replace('DockMonitorWidth=0','DockMonitorWidth=2560').Replace('DockMonitorHeight=0','DockMonitorHeight=1440').Replace('DockPixelScale=0','DockPixelScale=1').Replace('[!ZPos 1]','[!ZPos -2]')
 $main=$main.Replace('[Variables]',"[Variables]`r`nProductionScript=$repo\rainmeter\PrismGlass\@Resources\Dock.lua")
 $main | Set-Content -LiteralPath (Join-Path $skin 'Main.ini') -Encoding unicode
 Copy-Item -LiteralPath (Join-Path $repo 'rainmeter/PrismGlass/@Resources/LiquidGlass.lua') -Destination (Join-Path $skins '@Resources/LiquidGlass.lua')
 Add-Type -AssemblyName System.Drawing
 $png=Join-Path $fixture 'icon.png'
 $bitmap=[Drawing.Bitmap]::new(16,16)
 try{for($x=0;$x -lt 16;$x++){for($y=0;$y -lt 16;$y++){$bitmap.SetPixel($x,$y,[Drawing.Color]::CornflowerBlue)}};$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
 $icon=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($png))
 1..$IconCount | ForEach-Object {"pfixture$_`tRml4dHVyZQ==`t$icon`t1`t0`t1`t0"} | Set-Content -LiteralPath (Join-Path $fixture 'dock-state.txt') -Encoding ascii
 $csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $exe=Join-Path $out 'TestDockIconBridge.exe'
 & $csc /nologo /target:exe /platform:x64 ('/out:'+$exe) /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $repo 'tests/TestDockIconBridge.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorDock.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs')
 if($LASTEXITCODE -ne 0){throw 'Icon bridge test compilation failed'}
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 "Testing $IconCount real Image meters in a separate offscreen Rainmeter profile (2560x1440 geometry)." | Tee-Object -FilePath (Join-Path $out 'dock-icon-bridge-checks.txt')
 & $exe $fixture $process.Id 2>&1 | Tee-Object -FilePath (Join-Path $out 'dock-icon-bridge-checks.txt') -Append
 if($LASTEXITCODE -ne 0){throw 'Native icon bridge test failed; see artifacts/tests/dock-icon-bridge-checks.txt'}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000) | Out-Null;$process.Dispose()}
 if(Test-Path -LiteralPath (Join-Path $fixture 'Rainmeter.log')){Copy-Item -LiteralPath (Join-Path $fixture 'Rainmeter.log') -Destination (Join-Path $out 'dock-icon-bridge-rainmeter.log') -Force}
 if(Test-Path -LiteralPath $fixture){
  $resolved=(Resolve-Path -LiteralPath $fixture).Path
  $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
  if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
 }
}
