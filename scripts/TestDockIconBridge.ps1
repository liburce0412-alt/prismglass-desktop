param([string]$RainmeterPath='C:\Program Files\Rainmeter\Rainmeter.exe',[ValidateRange(1,32)][int]$IconCount=16,[ValidateRange(16,96)][int]$IconSize=48,[switch]$SweepOnly,[switch]$FunctionalIcons,[switch]$ExternalInput,[switch]$GeometryOnly,[switch]$OpacityFollow,[string]$ProductionScript,[string]$StateFile)
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
 $monitorWidth=2560;$monitorHeight=1440;$pixelScale=1
 if($ExternalInput -or $GeometryOnly -or $OpacityFollow){
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type @'
using System;using System.Runtime.InteropServices;
public static class DockFixtureDisplay {
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")]public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")]public static extern uint GetDpiForSystem();
}
'@
  $context=[DockFixtureDisplay]::SetThreadDpiAwarenessContext([IntPtr](-4))
  try{$monitorWidth=[DockFixtureDisplay]::GetSystemMetrics(0);$monitorHeight=[DockFixtureDisplay]::GetSystemMetrics(1);$pixelScale=[DockFixtureDisplay]::GetDpiForSystem()/96.0}finally{[void][DockFixtureDisplay]::SetThreadDpiAwarenessContext($context)}
 }
 $main=$main.Replace('DockMonitorLeft=0','DockMonitorLeft=-16000').Replace('DockMonitorTop=0','DockMonitorTop=-16000').Replace('DockMonitorWidth=0',"DockMonitorWidth=$monitorWidth").Replace('DockMonitorHeight=0',"DockMonitorHeight=$monitorHeight").Replace('DockPixelScale=0','DockPixelScale='+$pixelScale.ToString([Globalization.CultureInfo]::InvariantCulture)).Replace('DockIconSize=48',"DockIconSize=$IconSize").Replace('[!ZPos 0]','[!ZPos -2]')
 if(!$ProductionScript){$ProductionScript=Join-Path $repo 'rainmeter/PrismGlass/@Resources/Dock.lua'}
 $main=$main.Replace('[Variables]',"[Variables]`r`nProductionScript=$ProductionScript")
 $main+="`r`n[HintObserver]`r`nMeasure=Script`r`nScriptFile=$fixture\HintObserver.lua`r`n"
 @'
function Update()
 local meter=SKIN:GetMeter('DragHint')
 if meter then
  local file=io.open(SKIN:GetVariable('LiquidRoot')..'hint.txt','w')
  if file then file:write(meter:GetOption('Text'));file:close() end
  local hit=SKIN:GetMeter('Hit1')
  local tooltip=io.open(SKIN:GetVariable('LiquidRoot')..'tooltip.txt','w')
  if tooltip then tooltip:write(hit:GetOption('ToolTipText'));tooltip:close() end
 end
end
'@ | Set-Content -LiteralPath (Join-Path $fixture 'HintObserver.lua') -Encoding unicode
 $main | Set-Content -LiteralPath (Join-Path $skin 'Main.ini') -Encoding unicode
 Add-Type -AssemblyName System.Drawing
 $png=Join-Path $fixture 'icon.png'
 $bitmap=[Drawing.Bitmap]::new(16,16)
 try{for($x=0;$x -lt 16;$x++){for($y=0;$y -lt 16;$y++){$bitmap.SetPixel($x,$y,[Drawing.Color]::CornflowerBlue)}};$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
 $icon=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($png))
 $name=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('中文应用 · 测试'))
 if($FunctionalIcons){
  if($IconCount -lt 3){throw 'Functional icon fixture needs at least three slots'}
  Copy-Item -LiteralPath $png -Destination (Join-Path $fixture 'workspace.png')
  Copy-Item -LiteralPath $png -Destination (Join-Path $fixture 'trash-full.png')
 }
 1..$IconCount | ForEach-Object {
  $id="pfixture$_";$image=$icon
  if($FunctionalIcons -and $_ -eq 1){$id='workspace';$image=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Join-Path $fixture 'workspace.png')))}
  if($FunctionalIcons -and $_ -eq $IconCount){$id='trash';$image=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Join-Path $fixture 'trash-full.png')))}
  "$id`t$name`t$image`t1`t0`t1`t0"
 } | Set-Content -LiteralPath (Join-Path $fixture 'dock-state.txt') -Encoding ascii
 if($StateFile){
  $rows=Get-Content -LiteralPath $StateFile
  if($rows.Count -ne $IconCount){throw 'State snapshot count differs from the requested fixture icon count'}
  $rows | ForEach-Object {$_ -replace '\t[01]$',"`t0"} | Set-Content -LiteralPath (Join-Path $fixture 'dock-state.txt') -Encoding ascii
 }
 $csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 $exe=Join-Path $out 'TestDockIconBridge.exe'
 & $csc /nologo /target:exe /platform:x64 ('/out:'+$exe) /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $repo 'tests/TestDockIconBridge.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorDock.cs') (Join-Path $repo 'src/LiquidDesktop/RainmeterCommands.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs')
 if($LASTEXITCODE -ne 0){throw 'Icon bridge test compilation failed'}
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 "Testing $IconCount real Image meters in a separate offscreen Rainmeter profile (${monitorWidth}x${monitorHeight}, scale $pixelScale, icon $IconSize)." | Tee-Object -FilePath (Join-Path $out 'dock-icon-bridge-checks.txt')
 $testArgs=@($fixture,$process.Id);if($SweepOnly){$testArgs+='--sweep-only'};if($FunctionalIcons){$testArgs+='--functional-icons'};if($ExternalInput){$testArgs+='--external-input'};if($GeometryOnly){$testArgs+='--geometry-only'};if($OpacityFollow){$testArgs+='--opacity-follow'}
 & $exe @testArgs 2>&1 | Tee-Object -FilePath (Join-Path $out 'dock-icon-bridge-checks.txt') -Append
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
