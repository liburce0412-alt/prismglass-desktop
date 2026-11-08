param([string]$RainmeterPath='D:/Rainmeter.exe',[switch]$CaptureAmbient,[switch]$NewCardsOnly,[switch]$PhotosOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/widgets-'+[guid]::NewGuid().ToString('N'))
$skins=Join-Path $fixture 'Skins/PrismGlass'
$out=Join-Path $repo 'artifacts/tests'
$process=$null
New-Item -ItemType Directory -Force -Path $skins,$out | Out-Null
try{
 $names='Weather','System','Clock','Calendar','Battery','Folders','Music','Focus','Tools','BreathingOrb','DigitalClock','Moon','Photo','Photo2','Photo3','Photo4','Photo5','Photo6'
 if($NewCardsOnly){$names='BreathingOrb','DigitalClock','Moon','Photo'}
 if($PhotosOnly){$names='Photo','Photo2','Photo3'}
 New-Item -ItemType Directory -Path (Join-Path $fixture 'LiquidDesktop') -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo 'rainmeter/PrismGlass/@Resources') -Destination $skins -Recurse
 $settings="[Rainmeter]`r`nSkinPath=$fixture\Skins\`r`nLogging=1`r`nDisableVersionCheck=1`r`n"
 $photoIndex=0
 foreach($name in $names){
  $folder=Join-Path $skins $name;New-Item -ItemType Directory -Path $folder | Out-Null
  $text=[IO.File]::ReadAllText((Join-Path $repo "rainmeter/PrismGlass/$name/Main.ini")).Replace('{{INSTALL_ROOT}}',$fixture)
  # Exercise native meters without making a weather network request.
  $text=[regex]::Replace($text,'(?m)^URL=.*$','URL=')
  [IO.File]::WriteAllText((Join-Path $folder 'Main.ini'),$text,[Text.Encoding]::Unicode)
  $x=400;if($PhotosOnly){$x+=330*$photoIndex;$photoIndex++}
  $settings+="[PrismGlass\$name]`r`nActive=1`r`nWindowX=$x`r`nWindowY=180`r`nAlwaysOnTop=-2`r`nSavePosition=1`r`nDraggable=0`r`n"
 }
 foreach($file in Get-ChildItem -LiteralPath (Join-Path $skins '@Resources') -File | Where-Object Extension -in '.lua','.inc'){$text=[IO.File]::ReadAllText($file.FullName);[IO.File]::WriteAllText($file.FullName,$text,[Text.Encoding]::Unicode)}
 if($PhotosOnly){
  Add-Type -AssemblyName System.Drawing;$data=Join-Path $fixture 'LiquidDesktop/photo-cards';New-Item -ItemType Directory -Path $data|Out-Null
  foreach($item in @(@('Photo',600,400,290,197),@('Photo2',400,600,197,290),@('Photo3',400,400,290,290))){
   $token=[guid]::NewGuid().ToString('N');$bitmap=[Drawing.Bitmap]::new($item[1],$item[2]);$graphics=[Drawing.Graphics]::FromImage($bitmap);$gradient=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height),[Drawing.Color]::FromArgb(92,152,193),[Drawing.Color]::FromArgb(211,162,122),60)
   try{$graphics.FillRectangle($gradient,0,0,$bitmap.Width,$bitmap.Height);$graphics.FillEllipse([Drawing.Brushes]::LemonChiffon,$bitmap.Width*.66,$bitmap.Height*.12,$bitmap.Width*.15,$bitmap.Width*.15);$graphics.FillEllipse([Drawing.Brushes]::DarkSlateGray,-$bitmap.Width*.2,$bitmap.Height*.55,$bitmap.Width*1.4,$bitmap.Height);$bitmap.Save((Join-Path $data ($token+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$gradient.Dispose();$graphics.Dispose();$bitmap.Dispose()}
   [IO.File]::WriteAllText((Join-Path $data ($item[0]+'.txt')),($token+"`n"+$item[3]+"`n"+$item[4]+"`nFixture`n"),[Text.UTF8Encoding]::new($false))
  }
 }
 [IO.File]::WriteAllText((Join-Path $fixture 'Rainmeter.ini'),$settings,[Text.Encoding]::Unicode)
 [IO.File]::WriteAllText((Join-Path $fixture 'skin-path.txt'),$skins)
 if(!$CaptureAmbient){[IO.File]::WriteAllText((Join-Path $fixture 'LiquidDesktop/heartbeat.txt'),([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()+' Astro'))}
 $exe=Join-Path $out ($(if($CaptureAmbient){'TestAmbientPreview.exe'}else{'TestDesktopWidgets.exe'}))
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 ('/out:'+$exe) /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $repo 'tests/TestDesktopWidgets.cs') (Join-Path $repo 'src/LiquidDesktop/DesktopWidgets.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorDock.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs')
 if($LASTEXITCODE -ne 0){throw 'Desktop widget checks did not compile'}
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 $extraArgs=@();if($CaptureAmbient){$extraArgs+='--capture-ambient'};if($NewCardsOnly){$extraArgs+='--new-cards'};if($PhotosOnly){$extraArgs+='--photos-only'}
 & $exe $fixture $process.Id @extraArgs 2>&1 | Tee-Object -FilePath (Join-Path $out 'desktop-widgets.txt')
 if($LASTEXITCODE -ne 0){throw 'Desktop widget checks failed'}
 $rainmeterLog=Join-Path $fixture 'Rainmeter.log'
 if(Test-Path -LiteralPath $rainmeterLog){$log=Get-Content -LiteralPath $rainmeterLog -Raw;if($log -match '(?im)^ERRO.*(Ambient|BreathingOrb|DigitalClock|Moon|Photo)'){throw 'Decorative widget script or meter failed to load'} }
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000)|Out-Null;$process.Dispose()}
 $absolute=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped the workspace'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
 if(Test-Path -LiteralPath $absolute){throw 'Fixture cleanup failed'}
 if(Test-Path -LiteralPath $exe){Remove-Item -LiteralPath $exe}
}
