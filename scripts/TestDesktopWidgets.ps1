param([string]$RainmeterPath='D:/Rainmeter.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/widgets-'+[guid]::NewGuid().ToString('N'))
$skins=Join-Path $fixture 'Skins/PrismGlass'
$out=Join-Path $repo 'artifacts/tests'
$process=$null
New-Item -ItemType Directory -Force -Path $skins,$out | Out-Null
try{
 $names='Weather','System','Clock','Calendar','Battery','Folders','Music','Focus','Tools'
 New-Item -ItemType Directory -Path (Join-Path $fixture 'LiquidDesktop') -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo 'rainmeter/PrismGlass/@Resources') -Destination $skins -Recurse
 $settings="[Rainmeter]`r`nSkinPath=$fixture\Skins\`r`nLogging=1`r`nDisableVersionCheck=1`r`n"
 foreach($name in $names){
  $folder=Join-Path $skins $name;New-Item -ItemType Directory -Path $folder | Out-Null
  $text=[IO.File]::ReadAllText((Join-Path $repo "rainmeter/PrismGlass/$name/Main.ini")).Replace('{{INSTALL_ROOT}}',$fixture)
  # Exercise native meters without making a weather network request.
  $text=[regex]::Replace($text,'(?m)^URL=.*$','URL=')
  [IO.File]::WriteAllText((Join-Path $folder 'Main.ini'),$text,[Text.Encoding]::Unicode)
  $settings+="[PrismGlass\$name]`r`nActive=1`r`nWindowX=400`r`nWindowY=180`r`nAlwaysOnTop=-2`r`nSavePosition=1`r`nDraggable=0`r`n"
 }
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $skins '@Resources') -File -Filter '*.lua'){$text=[IO.File]::ReadAllText($file.FullName);[IO.File]::WriteAllText($file.FullName,$text,[Text.Encoding]::Unicode)}
 [IO.File]::WriteAllText((Join-Path $fixture 'Rainmeter.ini'),$settings,[Text.Encoding]::Unicode)
 [IO.File]::WriteAllText((Join-Path $fixture 'skin-path.txt'),$skins)
 [IO.File]::WriteAllText((Join-Path $fixture 'LiquidDesktop/heartbeat.txt'),([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()+' Astro'))
 $exe=Join-Path $out 'TestDesktopWidgets.exe'
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 ('/out:'+$exe) /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $repo 'tests/TestDesktopWidgets.cs') (Join-Path $repo 'src/LiquidDesktop/DesktopWidgets.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorDock.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs')
 if($LASTEXITCODE -ne 0){throw 'Desktop widget checks did not compile'}
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 & $exe $fixture $process.Id 2>&1 | Tee-Object -FilePath (Join-Path $out 'desktop-widgets.txt')
 if($LASTEXITCODE -ne 0){throw 'Desktop widget checks failed'}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000)|Out-Null;$process.Dispose()}
 $absolute=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped the workspace'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
 if(Test-Path -LiteralPath $absolute){throw 'Fixture cleanup failed'}
 if(Test-Path -LiteralPath $exe){Remove-Item -LiteralPath $exe}
}
