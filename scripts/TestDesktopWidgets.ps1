param([string]$RainmeterPath='D:/Rainmeter.exe',[switch]$CaptureAmbient,[switch]$NewCardsOnly,[switch]$PhotosOnly,[switch]$ExperienceOnly,[switch]$ClassicOnly,[switch]$LayoutOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/widgets-'+[guid]::NewGuid().ToString('N'))
$skins=Join-Path $fixture 'Skins/PrismGlass'
$out=Join-Path $repo 'artifacts/tests'
$process=$null
New-Item -ItemType Directory -Force -Path $skins,$out | Out-Null
try{
 $names='Weather','System','Clock','Calendar','Battery','Folders','Music','Focus','Tools','BreathingOrb','DigitalClock','Moon','Hourglass','SunPath','YearDots','Tide','Orrery','Bonsai','Photo','Photo2','Photo3','Photo4','Photo5','Photo6'
 if($NewCardsOnly){$names='BreathingOrb','DigitalClock','Moon','Photo'}
 if($PhotosOnly){$names='Photo','Photo2','Photo3'}
 if($ExperienceOnly){$names='Hourglass','SunPath','YearDots','Tide','Orrery','Bonsai'}
 if($ClassicOnly){$names='Clock','Calendar','System','Moon'}
 if($LayoutOnly){$names='Clock','Calendar','System','Moon'}
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
 if($ExperienceOnly){
  $probe="`nfunction ProbeTooltip() local f=io.open(SKIN:GetVariable('LiquidRoot')..'tooltip-'..SKIN:GetVariable('WidgetKind')..'.txt','w');if f then f:write(SKIN:GetMeter('Input'):GetOption('ToolTipHidden'));f:close()end end`n"
  $probe+="`nfunction ProbeWork() local f=io.open(SKIN:GetVariable('LiquidRoot')..'work-'..SKIN:GetVariable('WidgetKind')..'.txt','w');if f then f:write(SKIN:GetMeter('Caption'):GetOption('Text'));f:close()end end`n"
  $probe+="`nfunction ProbeControls() local f=io.open(SKIN:GetVariable('LiquidRoot')..'work-controls.txt','w');if f then for _,n in ipairs({'Preset3','Primary','Secondary'})do local m=SKIN:GetMeter(n);f:write(n..' '..m:GetX()..' '..m:GetY()..' '..m:GetW()..' '..m:GetH()..'\n')end;f:close()end end`n"
  [IO.File]::AppendAllText((Join-Path $skins '@Resources/WidgetArt.lua'),$probe,[Text.Encoding]::Unicode)
  # Deterministic sample, never copied from the user's location or weather cache.
  $now=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds();$day=[Math]::Floor(($now+28800)/86400)*86400-28800
  $body=@{utc_offset_seconds=28800;daily=@{sunrise=@($day+21600);sunset=@($day+64800)}}|ConvertTo-Json -Compress
  @{fetchedAt=$now*1000;body=$body}|ConvertTo-Json -Compress|Set-Content -LiteralPath (Join-Path $fixture 'LiquidDesktop/weather-cache.json') -Encoding utf8
  [IO.File]::WriteAllText((Join-Path $fixture 'LiquidDesktop/widget-work-context.txt'),"stamp=$now`nversion=1`ntasksAvailable=1`ntaskPending=2`ntaskTotal=3`ntaskFirst=核对设计稿的最后一页`ncountdownTitle=作品提交`ncountdownDate=2026-11-20`nfocusAvailable=1`nfocusTotal=1500`nfocusRemaining=1300`nfocusDeadline=0`nfocusCompleted=2`nweatherTemp=21`nweatherCode=2`nweatherLocation=杭州 · 示例天气",[Text.UTF8Encoding]::new($false))
 }
 if($ClassicOnly){
  $probe="`nfunction ProbeClock() local f=io.open(SKIN:GetVariable('LiquidRoot')..'clock-probe.txt','w');if f then f:write(SKIN:GetMeter('Hands'):GetOption('Shape3')..'|divider='..SKIN:GetMeasure('Ambient'):GetOption('UpdateDivider'));f:close()end end`n"
  [IO.File]::AppendAllText((Join-Path $skins '@Resources/Clock.lua'),$probe,[Text.Encoding]::Unicode)
 }
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
 if(!$CaptureAmbient -or $ClassicOnly){[IO.File]::WriteAllText((Join-Path $fixture 'LiquidDesktop/heartbeat.txt'),([DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()+' Astro'))}
 $exe=Join-Path $out ($(if($CaptureAmbient){'TestAmbientPreview.exe'}else{'TestDesktopWidgets.exe'}))
 $test=if($LayoutOnly){'tests/TestWidgetLayout.cs'}else{'tests/TestDesktopWidgets.cs'}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 ('/out:'+$exe) /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/WidgetPositions.cs') (Join-Path $repo 'src/LiquidDesktop/DesktopPreferences.cs') (Join-Path $repo 'src/LiquidDesktop/RuntimePolicy.cs') (Join-Path $repo $test) (Join-Path $repo 'src/LiquidDesktop/DesktopWidgets.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorDock.cs') (Join-Path $repo 'src/LiquidDesktop/RainmeterCommands.cs') (Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs')
 if($LASTEXITCODE -ne 0){throw 'Desktop widget checks did not compile'}
 $process=Start-Process -FilePath $RainmeterPath -ArgumentList ('"'+$fixture+'\Rainmeter.ini"') -WindowStyle Hidden -PassThru
 $extraArgs=@();if($CaptureAmbient){$extraArgs+='--capture-ambient'};if($NewCardsOnly){$extraArgs+='--new-cards'};if($PhotosOnly){$extraArgs+='--photos-only'};if($ExperienceOnly){$extraArgs+='--experience-only'};if($ClassicOnly){$extraArgs+='--classic-only'}
 & $exe $fixture $process.Id @extraArgs 2>&1 | Tee-Object -FilePath (Join-Path $out 'desktop-widgets.txt')
 if($LASTEXITCODE -ne 0){throw 'Desktop widget checks failed'}
 $rainmeterLog=Join-Path $fixture 'Rainmeter.log'
 if(Test-Path -LiteralPath $rainmeterLog){$log=Get-Content -LiteralPath $rainmeterLog -Raw;if($log -match '(?im)^ERRO.*(Ambient|WidgetArt|BreathingOrb|DigitalClock|Clock|Calendar|System|Moon|Hourglass|SunPath|YearDots|Tide|Orrery|Bonsai|Photo)'){throw 'Widget script or meter failed to load'} }
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.WaitForExit(5000)|Out-Null;$process.Dispose()}
 $absolute=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped the workspace'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
 if(Test-Path -LiteralPath $absolute){throw 'Fixture cleanup failed'}
 if(Test-Path -LiteralPath $exe){Remove-Item -LiteralPath $exe}
}
