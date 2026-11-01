param([string]$AppRoot='artifacts/experience-build/LiquidDesktop',[switch]$TopButtons)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/dock-visual-'+[guid]::NewGuid().ToString('N'))
$output=Join-Path $repo 'artifacts/dock-polish-20261101'
$app=[IO.Path]::GetFullPath((Join-Path $repo $AppRoot))
$process=$null
try{
 New-Item -ItemType Directory -Path $fixture,$output -Force|Out-Null
 foreach($name in @('control-icons.svg','index.html','island.html','island.js','island.css','glass-typography.css','glass-tooltip.css','desktop.js','glass-renderer.js','lyrics-style.js','dock-lyrics.css','desktop-preferences.js','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $app $name) -Destination $fixture}
 Add-Type -AssemblyName System.Drawing
 $bitmap=[Drawing.Bitmap]::new(1000,700);$g=[Drawing.Graphics]::FromImage($bitmap);$brush=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new(1000,700),[Drawing.Color]::FromArgb(25,74,145),[Drawing.Color]::FromArgb(140,64,180));$g.FillRectangle($brush,0,0,1000,700)
 $g.FillEllipse([Drawing.Brushes]::LightSkyBlue,430,400,380,190);$bitmap.Save((Join-Path $fixture 'wallpaper.png'));$brush.Dispose();$g.Dispose();$bitmap.Dispose()
 $exe=Join-Path $fixture 'TestDockLabel.exe'
 $case=if($TopButtons){'TestTopTooltip.cs'}else{'TestDockLabel.cs'}
 $log=if($TopButtons){'top-tooltip-checks.txt'}else{'checks.txt'}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$fixture/Microsoft.Web.WebView2.Core.dll" "/r:$fixture/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo ('tests/'+$case))
 if($LASTEXITCODE -ne 0){throw 'Visual test compilation failed'}
 $arguments='"'+$fixture+'" "'+$output+'" "'+(Join-Path $app 'LiquidDesktop.exe')+'"'
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(25000)){throw 'Visual test timed out'}
 Get-Content (Join-Path $output $log)
 if($process.ExitCode -ne 0){throw 'Visual test failed'}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $absolute=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture path'}
 for($attempt=0;$attempt -lt 30 -and (Test-Path -LiteralPath $absolute);$attempt++){try{Remove-Item -LiteralPath $absolute -Recurse -Force}catch{if($attempt -eq 29){throw};Start-Sleep -Milliseconds 200}}
}
