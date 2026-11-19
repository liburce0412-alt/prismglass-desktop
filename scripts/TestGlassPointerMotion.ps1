param([string]$CacheRoot='D:/Cache/PrismGlass',[string]$Label='current',[switch]$Dock,[string]$OutputRoot='artifacts/iteration5-20261112/glass',[string]$SourceRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=[IO.Path]::GetFullPath((Join-Path $repo $OutputRoot))
$app=Join-Path $out 'app'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Path $app -Force | Out-Null
try {
 if(!$SourceRoot){$SourceRoot=Join-Path $repo 'src/LiquidDesktop'}
 Get-ChildItem -LiteralPath $SourceRoot -File | Where-Object Extension -in '.js','.css','.html','.svg' | Copy-Item -Destination $app
 Copy-Item -LiteralPath (Join-Path $repo 'tests/GlassEdgeFlowProbe.js') -Destination (Join-Path $app 'edge-probe.js')
 Add-Type -AssemblyName System.Drawing
 $bitmap=[Drawing.Bitmap]::new(2560,1600);$g=[Drawing.Graphics]::FromImage($bitmap)
 try{$g.Clear([Drawing.Color]::SteelBlue);$g.FillEllipse([Drawing.Brushes]::LightSkyBlue,200,50,1500,1000);$bitmap.Save((Join-Path $app 'wallpaper.png'))}finally{$g.Dispose();$bitmap.Dispose()}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $app
 $sources=@('tests/TestGlassPointerMotion.cs','src/LiquidDesktop/DesktopPointerInput.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/DockPlate.cs','src/LiquidDesktop/WebViewPixelScale.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /platform:x64 /target:exe "/out:$app/TestGlassPointerMotion.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Pointer fixture compilation failed'}
 $result=Join-Path $out "$Label.json"
 $process=Start-Process -FilePath "$app/TestGlassPointerMotion.exe" -ArgumentList ('"'+$app+'" "'+$result+'" '+$(if($Dock){'dock'}else{'widgets'})) -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(18000)){throw 'Pointer fixture timed out'}
 if($process.ExitCode -ne 0){Get-Content -LiteralPath "$result.error";throw 'Pointer fixture failed'}
 Get-Content -LiteralPath $result
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $app){
  $resolved=(Resolve-Path -LiteralPath $app).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture cleanup escaped task scope'}
  Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($resolved)} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}
  for($attempt=0;$attempt -lt 12;$attempt++){try{Remove-Item -LiteralPath $resolved -Recurse -Force;break}catch{if($attempt -eq 11){throw};Start-Sleep -Milliseconds 250}}
 }
}
