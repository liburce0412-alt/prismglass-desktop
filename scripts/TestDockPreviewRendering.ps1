param([string]$CacheRoot='D:/Cache/PrismGlass')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/iteration6-20261112/preview'
$app=Join-Path $out 'runtime'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
New-Item -ItemType Directory -Path $app -Force | Out-Null
$process=$null
try {
 foreach($name in @('glass-materials.js','glass-renderer.js','native-glass.html')){Copy-Item -LiteralPath (Join-Path $repo "src/LiquidDesktop/$name") -Destination $app -Force}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app -Force}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $app -Force
 Set-Content -LiteralPath (Join-Path $out 'checks.txt') -Value ''
 $sources=@('tests/TestDockPreviewRendering.cs','src/LiquidDesktop/SharedWindowCapture.cs','src/LiquidDesktop/LiveGlassBackdrop.cs','src/LiquidDesktop/OpticalBackdrop.cs','src/LiquidDesktop/WindowCapture.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/NativeGlassMaterial.cs','src/LiquidDesktop/MonitorWallpaper.cs','src/LiquidDesktop/WebViewPixelScale.cs','src/LiquidDesktop/WebViewVisibility.cs','src/LiquidDesktop/DockBackend.cs','src/LiquidDesktop/IslandEvents.cs','src/LiquidDesktop/DockPreview.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/MonitorCatalog.cs','src/LiquidDesktop/RuntimePolicy.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestDockPreviewRendering.exe" /r:System.Numerics.dll /r:System.Numerics.Vectors.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Native preview fixture compilation failed'}
 $process=Start-Process -FilePath "$app/TestDockPreviewRendering.exe" -ArgumentList ('"'+$app+'" "'+$out+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(25000)){throw 'Native preview fixture timed out'}
 Get-Content -LiteralPath (Join-Path $out 'checks.txt')
 if($process.ExitCode -ne 0){throw 'Native preview fixture failed'}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $app){$resolved=(Resolve-Path -LiteralPath $app).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Preview cleanup escaped artifacts'};for($i=0;$i -lt 30;$i++){try{Remove-Item -LiteralPath $resolved -Recurse -Force;break}catch{if($i -eq 29){throw};Start-Sleep -Milliseconds 200}};if(Test-Path -LiteralPath $resolved){throw 'Preview fixture cleanup failed'}}
}
